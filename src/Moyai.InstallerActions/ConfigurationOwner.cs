using System.Security.Principal;
using WixToolset.Dtf.WindowsInstaller;

namespace Moyai.InstallerActions;

/// <summary>MSIの保存済み所有者SIDを権限設定用の完全修飾アカウント名へ解決します。</summary>
public static class ConfigurationOwner
{
    /// <summary>権限やファイルを変更せず、MSIセッション内だけに所有者名を設定します。</summary>
    [CustomAction]
    public static ActionResult ResolveConfigurationOwner(Session session)
    {
        if (session is null) return ActionResult.Failure;
        try
        {
            var owner = new SecurityIdentifier(session["MOYAI_CONFIG_OWNER_SID"]);
            var account = (NTAccount)owner.Translate(typeof(NTAccount));
            if (!owner.Equals(account.Translate(typeof(SecurityIdentifier))))
            {
                session.Log("Configuration owner account did not resolve to the configured SID.");
                return ActionResult.Failure;
            }

            session["MOYAI_CONFIG_OWNER_ACCOUNT"] = account.Value;
            return ActionResult.Success;
        }
        catch (Exception exception)
        {
            session.Log("Configuration owner resolution failed ({0}); no permissions were granted.", exception.GetType().Name);
            return ActionResult.Failure;
        }
    }
}
