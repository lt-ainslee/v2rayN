namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigSingboxService
{
    private void GenLog(bool isSpeedtest = false)
    {
        try
        {
            switch (_config.CoreBasicItem.Loglevel)
            {
                case "debug":
                case "info":
                case "error":
                    _coreConfig.log.level = _config.CoreBasicItem.Loglevel;
                    break;

                case "warning":
                    _coreConfig.log.level = "warn";
                    break;

                default:
                    break;
            }
            if (_config.CoreBasicItem.Loglevel == Global.None)
            {
                _coreConfig.log.disabled = true;
            }
            // Speed test cores run without elevation; on Linux/macOS they cannot write to log
            // files a root TUN core created first, and the core then exits at startup
            if (_config.CoreBasicItem.LogEnabled && !isSpeedtest)
            {
                var dtNow = DateTime.Now;
                _coreConfig.log.output = Utils.GetLogPath($"sbox_{dtNow:yyyy-MM-dd}.txt");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }
}
