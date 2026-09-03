using System;
using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    public static class RuntimeDiagnostics
    {
        public static Action<string> InfoSink;
        public static Action<string> WarningSink;
        public static Action<string> ErrorSink;

        public static void Info(string message)
        {
            if (InfoSink != null)
                InfoSink(message);
            else
                Debug.Log("Tobacco pot and cigar: " + message);
        }

        public static void Warning(string message)
        {
            if (WarningSink != null)
                WarningSink(message);
            else
                Debug.LogWarning("Tobacco pot and cigar: " + message);
        }

        public static void Error(string message)
        {
            if (ErrorSink != null)
                ErrorSink(message);
            else
                Debug.LogError("Tobacco pot and cigar: " + message);
        }

        public static void Reset()
        {
            InfoSink = null;
            WarningSink = null;
            ErrorSink = null;
        }
    }
}
