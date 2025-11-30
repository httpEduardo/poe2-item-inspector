using System;

namespace PoE2Inspector.Services.Logging;

public interface ILogService
{
    void Info(string msg);
    void Warn(string msg);
    void Error(string msg, Exception? ex = null);
}
