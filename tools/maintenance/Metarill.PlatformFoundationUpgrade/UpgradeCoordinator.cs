using Metarill.Data.Schema;
namespace Metarill.Tools.PlatformFoundationUpgrade;

/// <summary>两阶段升级编排；确认/实时预览先于写入，任何阶段失败均停止，绝不自动回退或重试DDL。</summary>
internal static class UpgradeCoordinator
{
    /// <summary>数据库工厂延迟到计划确认之后；独立核验用新的会话，不能把内存状态当作提交证明。</summary>
    internal static async Task ApplyAsync(UpgradePlan reviewed,string confirmedSha,Func<IUpgradeDatabase> open,Action<string,object?> record,CancellationToken token)
    {
        UpgradePlanCodec.Confirm(reviewed,confirmedSha);
        var phase="revalidate";var knownDdl=0;var metadataState="not-started";
        try
        {
            using(var database=open())
            {
                var current=await database.PreviewAsync(reviewed.ServerUuid,token);
                if(UpgradePlanCodec.Digest(current)!=confirmedSha)throw new InvalidOperationException("当前源、目标或结构/元数据状态已变化，请重新预览。");
                record("validated",new{planSha256=confirmedSha,metadataRows=reviewed.Rows.Count,statements=reviewed.Changes.Count});
                phase="ddl";record("ddl-started",null);var result=await database.ApplySchemaAsync(token);knownDdl=result.AppliedStatements;
                if(!result.Succeeded||result.AppliedStatements!=reviewed.Changes.Count||result.Database!=reviewed.Database)throw new InvalidOperationException("结构阶段未确认完整审查范围。");
                record("ddl-committed",new{knownCompletedStatements=knownDdl});
                phase="metadata";metadataState="outcome-unknown";record("metadata-started",null);await database.SeedAsync(token);metadataState="committed";record("metadata-committed",new{rows=reviewed.Rows.Count});
            }
            phase="independent-verification";using var independent=open();var verified=await independent.VerifyAsync(reviewed.ServerUuid,token);
            if(verified.MatchingRows!=reviewed.Rows.Count||verified.PendingChanges!=0||verified.Fingerprint!=reviewed.DesiredFingerprint)throw new InvalidOperationException("独立核验未达到审查目标。");
            record("verified",verified);
        }
        catch(Exception error)
        {
            if(error is SchemaApplyException schema)knownDdl=schema.Result.AppliedStatements;
            // 失败语句/提交可能已生效；只报已确认数量与不确定状态，不声称整体回滚或擅自修复。
            try{record("stopped",new{phase,knownCompletedStatements=knownDdl,ddlInFlightOutcomeUnknown=phase=="ddl",metadataState,errorType=error.GetType().Name});}
            catch(Exception receiptError){error.Data["ReceiptFailureType"]=receiptError.GetType().Name;}
            throw;
        }
    }
}
