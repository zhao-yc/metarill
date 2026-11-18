using System.Globalization;
using Metarill.Core.Cascades;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    private sealed record SelfTreeLabelProof(TableMetadata Target,ColumnMetadata Name,ColumnMetadata Parent,
        PageMetadata Page,FixedConditionSqlPlan? Condition);

    /// <summary>只将原控件的叶子名称标为逻辑来源，完整路径显示文本仍由原前端合同输出。</summary>
    private static bool IsSelfTreeLabel(ColumnMetadata control,string field)=>
        control.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&
        field==control.ColumnName[..^3]+"_name"&&
        Value(Definition(control),"sourceMode")=="selfTree";

    /// <summary>读取原自树配置、物理父键及唯一可读页。维持非凭据叶名和固定范围约束。</summary>
    private static SelfTreeLabelProof SelfTreeLabel(MetadataCatalog catalog,ColumnMetadata control)
    {
        var definition=Definition(control);
        var tree=definition.GetProperty("selfTree");
        if(tree.ValueKind!=System.Text.Json.JsonValueKind.Object||
            tree.EnumerateObject().Select(pair=>pair.Name).Distinct(StringComparer.Ordinal).Count()!=tree.EnumerateObject().Count()||
            new[]{"whereSql","where","sql","pageId"}.Any(key=>definition.TryGetProperty(key,out _)||tree.TryGetProperty(key,out _))||
            Value(tree,"idField") is not("" or "id")||Value(tree,"rootMode") is not("" or "blank")||
            Value(tree,"filterMatchMode") is not("" or "exact" or "selfAndDescendants"))
            throw new NotSupportedException("多值名称的自树定义不属于原固定根、主键和范围。");
        var target=catalog.ConfigTables.Single(table=>table.TableName==Value(tree,"table"));
        if(target.YzProjectId.Length>0&&target.YzProjectId!=catalog.YzProjectId||target.IsRongyu is not(0 or 1)||
            target.IsView!=0||target.IsFunctionTable!=0||control.LinkConfigTableId.Length>0&&control.LinkConfigTableId!=target.Id)
            throw new NotSupportedException("多值名称的自树目标不属于当前已发布实体。");
        var members=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id));
        var storage=MetadataStoragePolicy.ForTable(catalog,target);
        var parent=members.Single(column=>column.ColumnName==Value(tree,"parentField"));
        var name=members.Single(column=>column.ColumnName==(Value(tree,"nameField") is {Length:>0} field?field:"name"));
        if(parent.ColumnName is "id" or "yz_project_id" or "insert_manager_id" or "update_manager_id" or "insert_user_id" or "update_user_id"||
            parent.LogicalType is not("string" or "int64")||!GlobalPhysicalRelationValue.IsScalar(storage,target,parent)||
            parent.LinkConfigTableId.Length>0&&parent.LinkConfigTableId!=target.Id||
            name.Id==parent.Id||name.ColumnName=="id"||name.LogicalType is not("string" or "int32" or "int64")||
            name.IsVirtualColumn!=0||ExactDataManageValues.IsSensitiveField(name.ColumnName)||
            (storage.IsPhysical(name)?!GlobalPhysicalRelationValue.IsScalar(storage,target,name)&&
                !GlobalPhysicalRelationValue.IsPublishedIntegerCache(catalog,storage,target,name):
                storage.Kind(name)!=MetadataFieldStorage.Join||target.IsRongyu!=0))
            throw new NotSupportedException("多值名称的自树父键或标量叶名无效。");
        var (page,condition)=CascadeLabelPage(catalog,target,name.ColumnName,parent.ColumnName);
        if(GlobalCrossTableTarget.Declared(target))
        {
            GlobalCrossTableTarget.RequireSelfTreeName(catalog,target,page,name);
        }
        else if(!members.Any(column=>column.ColumnName=="yz_project_id"&&storage.IsPhysical(column)))
            throw new NotSupportedException("自树节点缺少真实项目范围。");
        return new(target,name,parent,page,condition);
    }

    private sealed partial class Builder
    {
        private int selfTreeLabelCount;

        /// <summary>沿原64层、活动节点与根语义检查完整祖先链，取该链的真实叶名。</summary>
        private string SelfTreeLabelExpression(TableMetadata table,string owner,ColumnMetadata control,ColumnMetadata column)
        {
            if(!storage[table.Id].IsPhysical(control)||control.LogicalType is not("int64" or "string"))
                throw new NotSupportedException("自树多值名称要求物理选择位。");
            var proof=SelfTreeLabel(catalog,control);
            ValidateTarget(proof.Target);
            cascadeLabelPages.Add(proof.Page.Id);
            if(Text(column,"columnFullName") is {Length:>0} path&&
                path!=control.ColumnName[..^3]+"."+proof.Name.ColumnName&&path!=control.ColumnName+"."+proof.Name.ColumnName)
                throw new NotSupportedException("自树名称的已声明来源路径与原控件不符。");
            var global=GlobalCrossTableTarget.Declared(proof.Target);
            var prefix=aliasPrefix+"tree"+selfTreeLabelCount+++"_";
            var seed=prefix+"leaf";
            var ancestor=prefix+"ancestor";
            var walk=prefix+"walk";
            var targetTable=Q(proof.Target.TableName);
            var source=owner+"."+Q(control.ColumnName);
            var leafKey=PositiveText("CAST("+source+" AS CHAR)");
            var max=SelfTreeReader.MaximumDepth.ToString(CultureInfo.InvariantCulture);

            var seedScope=NodeScope(seed);
            var ancestorScope=NodeScope(ancestor);
            var cte="WITH RECURSIVE "+walk+"(node_id,parent_key,depth,seen) AS (SELECT "+seed+".`id`,"+
                seed+"."+Q(proof.Parent.ColumnName)+",1,JSON_ARRAY(CAST("+seed+".`id` AS CHAR)) FROM "+targetTable+" "+seed+
                " WHERE BINARY CAST("+seed+".`id` AS CHAR)=BINARY "+leafKey+WithScope(seedScope)+
                " UNION ALL SELECT "+ancestor+".`id`,"+ancestor+"."+Q(proof.Parent.ColumnName)+","+walk+".depth+1,"+
                "JSON_ARRAY_APPEND("+walk+".seen,'$',CAST("+ancestor+".`id` AS CHAR)) FROM "+walk+" JOIN "+targetTable+" "+ancestor+
                " ON BINARY CAST("+ancestor+".`id` AS CHAR)=BINARY "+PositiveText("CAST("+walk+".parent_key AS CHAR)")+
                " WHERE "+walk+".depth<"+max+" AND NOT JSON_CONTAINS("+walk+".seen,JSON_QUOTE(CAST("+ancestor+".`id` AS CHAR)))"+
                WithScope(ancestorScope)+") ";
            var reachesRoot="("+cte+"SELECT 1 FROM "+walk+
                " WHERE "+walk+".parent_key IS NULL OR CAST("+walk+".parent_key AS CHAR) REGEXP '^[[:space:]]*$' LIMIT 1) IS NOT NULL";

            var display=prefix+"display";
            var nodeName=display+"."+Q(proof.Name.ColumnName);
            var nodeFrom=" FROM "+targetTable+" "+display;
            if(!storage[proof.Target.Id].IsPhysical(proof.Name))
            {
                var nested=new Builder(catalog,proof.Target.Id,matching:!global,rootAlias:display,aliasPrefix:prefix+"label_",
                    trackReverse:trackReverse,candidatePage:global&&HasGlobalRowProject(proof.Target)?proof.Page:null,multiLabel:true)
                    .Compile([proof.Name.ColumnName],[]);
                if(!nested.Origins.TryGetValue(proof.Name.ColumnName,out var terminal)||
                    ExactDataManageValues.IsSensitiveField(terminal.Column)||nested.MultiProjections.Count>0||nested.Crosses.Count>0||
                    nested.Types[proof.Name.ColumnName]!=proof.Name.LogicalType)
                    throw new NotSupportedException("自树叶名必须落到固定非凭据单值终点。");
                nodeName=nested.Expression(proof.Name.ColumnName);
                nodeFrom=nested.From;
                foreach(var pair in nested.Constants)crossConstants.Add(pair.Key,pair.Value);
                if(trackReverse)
                {
                    foreach(var reference in nested.PhysicalReferences)references.TryAdd((reference.Table,reference.Column.Name),reference);
                    foreach(var entry in nested.Reverse)
                    {
                        var predicate="((SELECT 1"+entry.From+" WHERE BINARY CAST("+display+".`id` AS CHAR)=BINARY "+leafKey+
                            WithScope(nested.Scope)+" AND "+entry.TargetPredicate+" LIMIT 1) IS NOT NULL)";
                        AddReverse(entry.TargetTable,predicate,entry.Outputs.Values.SelectMany(fields=>fields));
                    }
                }
            }
            if(trackReverse)
            {
                Use(table,control);
                Touch(owner,control.ColumnName);
                Use(proof.Target,Column(proof.Target,"id"));
                Use(proof.Target,proof.Parent);
                if(storage[proof.Target.Id].IsPhysical(proof.Name))Use(proof.Target,proof.Name);
                // 不依赖目标当前可见。父节点失效、删除或恢复时，仍从已选叶子反查原消费者。
                var predicate="(BINARY "+leafKey+"=BINARY @target_text OR ("+cte+"SELECT 1 FROM "+walk+
                    " WHERE "+walk+".node_id=@target_id OR BINARY "+PositiveText("CAST("+walk+".parent_key AS CHAR)")+
                    "=BINARY @target_text LIMIT 1) IS NOT NULL)";
                var fields=new[]{proof.Parent.ColumnName,proof.Name.ColumnName,"id","yz_project_id","delete_no"};
                AddReverse(proof.Target.TableName,predicate,fields.Concat(ConditionFields()));
                if(global)publishedArrayIdentity=true;
            }
            var label=MySqlMultiValueLabel.Text(nodeName,proof.Name.LogicalType);
            return "CASE WHEN "+reachesRoot+" THEN (SELECT "+label+nodeFrom+
                " WHERE BINARY CAST("+display+".`id` AS CHAR)=BINARY "+leafKey+" LIMIT 1) END";

            string NodeScope(string alias)
            {
                var scope=Scope(proof.Target,alias,projectScoped:!global);
                if(proof.Condition is null)return scope;
                var condition=CascadeLabelCondition(proof.Target,proof.Page,proof.Condition,alias,"mt_"+alias);
                return scope.Length==0?condition:scope+" AND "+condition;
            }

            IReadOnlySet<string> ConditionFields()=>FixedConditionPlan.Compile(proof.Page.WhereSql,
                columns[proof.Target.Id].Keys).Fields.ToHashSet(StringComparer.Ordinal);

            void AddReverse(string target,string predicate,IEnumerable<string> fields)
            {
                var dependencies=new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal)
                    {{output,fields.ToHashSet(StringComparer.Ordinal)}};
                customReverse.Add(new(target," FROM "+Q(root.TableName)+" "+rootAlias+string.Concat(joins),predicate,"predicate",dependencies));
            }

            static string WithScope(string scope)=>scope.Length==0?"":" AND "+scope;
        }
    }
}
