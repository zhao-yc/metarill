using System.Globalization;
using Metarill.Core.Cascades;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Relations;

namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    private sealed record SelfTreeLabelProof(TableMetadata Target,ColumnMetadata Name,ColumnMetadata Parent,
        PageMetadata Page,FixedConditionSqlPlan? Condition,bool Scoped);

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
            parent.LogicalType is not("string" or "int64")||!GlobalPhysicalRelationValue.IsScalar(storage,target,parent)&&!IsSelfTreeParent(target,storage,parent)||
            parent.LinkConfigTableId.Length>0&&parent.LinkConfigTableId!=target.Id||
            name.Id==parent.Id||name.ColumnName=="id"||name.LogicalType!="string"&&!MultiValueLabel.IsTyped(name.LogicalType)||
            name.IsVirtualColumn!=0||ExactDataManageValues.IsSensitiveField(name.ColumnName)||
            (storage.IsPhysical(name)?!GlobalPhysicalRelationValue.IsScalar(storage,target,name)&&
                !GlobalPhysicalRelationValue.IsPublishedTypedCache(catalog,storage,target,name):
                storage.Kind(name)!=MetadataFieldStorage.Join||target.IsRongyu!=0))
            throw new NotSupportedException("多值名称的自树父键或标量叶名无效。");
        var (page,condition)=CascadeLabelPage(catalog,target,name.ColumnName,parent.ColumnName);
        var scoped=FixedConditionPlan.Compile(page.WhereSql,members.Select(column=>column.ColumnName)).Fields
            .Any(field=>CrossTableStorageColumns.IsLogicalPathField(target,members,field));
        if(GlobalCrossTableTarget.Declared(target))
        {
            if(scoped)GlobalCrossTableTarget.RequireScopedSelfTreeName(catalog,target,page,name);
            else GlobalCrossTableTarget.RequireSelfTreeName(catalog,target,page,name);
        }
        else if(!members.Any(column=>column.ColumnName=="yz_project_id"&&storage.IsPhysical(column)))
            throw new NotSupportedException("自树节点缺少真实项目范围。");
        return new(target,name,parent,page,condition,scoped);
    }

    /// <summary>同表父字段也可以承载原树控件，只有真实物理自关联键允许同时作为拓扑列。</summary>
    private static bool IsSelfTreeParent(TableMetadata table,MetadataStoragePlan storage,ColumnMetadata column)
    {
        if(column.ControlKind!="CascadeTree"||!IsPhysicalTreeKey(table,storage,column))return false;
        var definition=Definition(column);
        return Value(definition,"sourceMode")=="selfTree"&&definition.TryGetProperty("selfTree",out var settings)&&
            Value(settings,"table")==table.TableName&&Value(settings,"parentField")==column.ColumnName&&
            (column.LinkConfigTableId.Length==0||column.LinkConfigTableId==table.Id);
    }

    private static bool IsPhysicalTreeKey(TableMetadata table,MetadataStoragePlan storage,ColumnMetadata column)=>
        column.ConfigTableId==table.Id&&column.IsVirtualColumn==0&&storage.IsPhysical(column)&&!CrossFlag(column,"isJoinColumn")&&
        column.LogicalType is "string" or "int64"&&column.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&
        !ExactDataManageValues.IsSensitiveField(column.ColumnName);

    private sealed partial class Builder
    {
        private int selfTreeLabelCount;

        /// <summary>只开放实际发布树的选择键和逻辑叶名。全局共享行不能依赖调用方的项目私有树。</summary>
        private bool PublishedGlobalSelfTreeField(ColumnMetadata column)
        {
            var rootStorage=storage[root.Id];
            if(matching)return false;
            if(!deferCascadeNames)return IsSelfTreeParent(root,rootStorage,column)&&SelfTreeLabel(catalog,column).Target.Id==root.Id;
            var control=column.ControlKind=="CascadeTree"?column:
                column.ColumnName.EndsWith("_name",StringComparison.Ordinal)?
                    columns[root.Id].GetValueOrDefault(column.ColumnName[..^5]+"_id"):null;
            if(control is not {ControlKind:"CascadeTree"}||Value(Definition(control),"sourceMode")!="selfTree"||
                !IsPhysicalTreeKey(root,rootStorage,control)||column.IsVirtualColumn!=0||
                column.Id!=control.Id&&(column.LogicalType!="string"||rootStorage.IsPhysical(column)||ExactDataManageValues.IsSensitiveField(column.ColumnName)))
                return false;
            var proof=SelfTreeLabel(catalog,control);
            if(!HasGlobalRowProject(root)&&!GlobalCrossTableTarget.Declared(proof.Target))
                throw new NotSupportedException("无行项目列的全局来源要求全局目标树。");
            ValidateTarget(proof.Target);
            cascadeLabelPages.Add(proof.Page.Id);
            return true;
        }

        /// <summary>沿原64层、活动节点与根语义检查完整祖先链，取该链的真实叶名。</summary>
        private string SelfTreeLabelExpression(TableMetadata table,string owner,ColumnMetadata control,ColumnMetadata column,string? publishedPageId=null)
        {
            if(!storage[table.Id].IsPhysical(control)||control.LogicalType is not("int64" or "string"))
                throw new NotSupportedException("自树多值名称要求物理选择位。");
            var proof=SelfTreeLabel(catalog,control);
            if(publishedPageId is not null&&(publishedPageId!=proof.Page.Id||!SelfTreeCache.UsesWholePath(catalog,table,publishedPageId)))
                throw new InvalidOperationException("自树缓存与实际固定目标页不一致。");
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
            var display=prefix+"display";
            // 名称副本的目标账号条件从来源行取值。普通候选仍绑定请求的真实身份。
            var cacheOwner=cascadeCacheOwner??(trackReverse&&(proof.Scoped||publishedPageId is not null)
                ?new CascadeCacheOwner(table,owner,publishedPageId is not null&&GlobalCrossTableTarget.Declared(table)):null);
            MySqlNonRedundantReadPlan? fixedTarget=null;
            if(proof.Scoped)
            {
                var fields=new[]{"id",proof.Name.ColumnName};
                if(cacheOwner is {} cached)
                {
                    // 维护使用保存行的归属，不能借发起账号覆盖其它行的名称。
                    fixedTarget=new Builder(catalog,proof.Target.Id,rootAlias:display,aliasPrefix:prefix+"s_",
                        trackReverse:trackReverse,multiLabel:true,cascadeLabelField:"id",cascadeCacheOwner:cached)
                    {
                        SelectedMultiPage=proof.Page,GlobalCrossCandidate=global,CrossAliasPrefix=prefix+"sx"
                    }.Compile(fields,[]);
                    foreach(var field in fixedTarget.CascadeCacheOwnerFields)
                    {
                        Use(cached.Table,Column(cached.Table,field));
                        Touch(cached.Alias,field);
                    }
                    cascadeCacheOwnerFields.UnionWith(fixedTarget.CascadeCacheOwnerFields);
                }
                else fixedTarget=CompileCrossPageCandidate(catalog,proof.Target.Id,proof.Page.Id,
                    fields,[],rootAlias:display,aliasPrefix:prefix+"s_");
                fixedTarget.CopyParametersTo(crossConstants);
                foreach(var reference in fixedTarget.PhysicalReferences)
                    references.TryAdd((reference.Table,reference.Column.Name),reference);
                if(publishedPageId is not null)
                {
                    if(!fixedTarget.Origins.TryGetValue(proof.Name.ColumnName,out var valueOrigin)||
                        ExactDataManageValues.IsSensitiveField(valueOrigin.Column))
                        throw new NotSupportedException("自树缓存名称缺少固定非凭据单值来源。");
                    origin=valueOrigin;
                }
                cascadeLabelPages.UnionWith(fixedTarget.CascadeLabelPages);
                if(fixedTarget.RequiresPublishedProjectIdentity)scopedGlobalCrossTarget=true;
            }

            var cte=WalkCte(NodeScope(seed),NodeScope(ancestor));
            var reachesRoot="("+cte+"SELECT 1 FROM "+walk+
                " WHERE "+walk+".parent_key IS NULL OR CAST("+walk+".parent_key AS CHAR) REGEXP '^[[:space:]]*$' LIMIT 1) IS NOT NULL";

            var nodeName=fixedTarget?.Expression(proof.Name.ColumnName)??display+"."+Q(proof.Name.ColumnName);
            var nodeFrom=fixedTarget?.From??" FROM "+targetTable+" "+display;
            if(publishedPageId is not null&&fixedTarget is null&&storage[proof.Target.Id].IsPhysical(proof.Name))
                origin=new(proof.Target.TableName,proof.Name.ColumnName);
            if(fixedTarget is null&&!storage[proof.Target.Id].IsPhysical(proof.Name))
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
                if(publishedPageId is not null)origin=terminal;
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
                // 失效节点不能截断反向路径；名称重算再执行各层实际可见性。
                var reverseCte=fixedTarget is null&&publishedPageId is null?cte:WalkCte(
                    Scope(proof.Target,seed,projectScoped:!global,includeDeleted:true),
                    Scope(proof.Target,ancestor,projectScoped:!global,includeDeleted:true));
                if(fixedTarget is not null)
                    foreach(var reverse in fixedTarget.Reverse)
                    {
                        var probe="EXISTS (SELECT 1"+reverse.From+" WHERE "+display+".`id`="+walk+".node_id AND "+reverse.TargetPredicate+")";
                        AddReverse(reverse.TargetTable,"("+reverseCte+"SELECT 1 FROM "+walk+" WHERE "+probe+" LIMIT 1) IS NOT NULL",
                            reverse.Outputs.Values.SelectMany(fields=>fields));
                    }
                var predicate="(BINARY "+leafKey+"=BINARY @target_text OR ("+reverseCte+"SELECT 1 FROM "+walk+
                    " WHERE "+walk+".node_id=@target_id OR BINARY "+PositiveText("CAST("+walk+".parent_key AS CHAR)")+
                    "=BINARY @target_text LIMIT 1) IS NOT NULL)";
                var fields=new[]{proof.Parent.ColumnName,proof.Name.ColumnName,"id","yz_project_id","delete_no"};
                AddReverse(proof.Target.TableName,predicate,fields.Concat(ConditionFields()));
                if(global)publishedArrayIdentity=true;
            }
            var label=MySqlMultiValueLabel.Text(nodeName,proof.Name.LogicalType);
            var empty=publishedPageId is not null&&control.LogicalType=="string"?" WHEN BINARY "+source+"=BINARY '' THEN ''":"";
            return "CASE"+empty+" WHEN "+reachesRoot+" THEN (SELECT "+label+nodeFrom+
                " WHERE BINARY CAST("+display+".`id` AS CHAR)=BINARY "+leafKey+" LIMIT 1) END";

            // 同一拓扑用于实际可见读取和失效后的反向定位。
            string WalkCte(string seedScope,string ancestorScope)=>
                "WITH RECURSIVE "+walk+"(node_id,parent_key,depth,seen) AS (SELECT "+seed+".`id`,"+
                seed+"."+Q(proof.Parent.ColumnName)+",1,JSON_ARRAY(CAST("+seed+".`id` AS CHAR)) FROM "+targetTable+" "+seed+
                " WHERE BINARY CAST("+seed+".`id` AS CHAR)=BINARY "+leafKey+WithScope(seedScope)+
                " UNION ALL SELECT "+ancestor+".`id`,"+ancestor+"."+Q(proof.Parent.ColumnName)+","+walk+".depth+1,"+
                "JSON_ARRAY_APPEND("+walk+".seen,'$',CAST("+ancestor+".`id` AS CHAR)) FROM "+walk+" JOIN "+targetTable+" "+ancestor+
                " ON BINARY CAST("+ancestor+".`id` AS CHAR)=BINARY "+PositiveText("CAST("+walk+".parent_key AS CHAR)")+
                " WHERE "+walk+".depth<"+max+" AND NOT JSON_CONTAINS("+walk+".seen,JSON_QUOTE(CAST("+ancestor+".`id` AS CHAR)))"+
                WithScope(ancestorScope)+") ";

            string NodeScope(string alias)
            {
                // 候选仅在本节点命中时通过，根、叶和每个祖先都执行同页逻辑条件。
                if(fixedTarget is not null)
                    return "EXISTS (SELECT 1"+fixedTarget.From+" WHERE "+fixedTarget.Expression("id")+"="+alias+".`id`"+
                        WithScope(fixedTarget.Scope)+")";
                var scope=Scope(proof.Target,alias,projectScoped:!global);
                if(proof.Condition is null)return scope;
                var condition=CascadeLabelCondition(proof.Target,proof.Page,proof.Condition,alias,"mt_"+alias,cacheOwner);
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
