using System.IO;

namespace VSCodeDebugging.VSCodeProtocol
{
  public static class PlatformUtil
  {
    public static readonly bool IsWindows = Path.DirectorySeparatorChar == '\\';
  }
}