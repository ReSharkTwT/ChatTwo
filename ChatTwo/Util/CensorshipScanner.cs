using System;
using System.Runtime.InteropServices;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.String;

namespace ChatTwo.Util
{
    public static unsafe class CensorshipScanner
    {
        private const string GetFilteredUtf8StringSig = "48 89 74 24 ?? 57 48 83 EC ?? 48 83 79 ?? ?? 48 8B FA 48 8B F1 0F 84 ?? ?? ?? ?? 48 89 5C 24";
        private const string VulgarInstanceOffsetSig = "48 8B 81 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B D3";

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void GetFilteredUtf8StringDelegate(IntPtr vulgarInstance, Utf8String* utf8String);

        private static GetFilteredUtf8StringDelegate? GetFilteredFunc;
        private static IntPtr VulgarInstanceOffset = IntPtr.Zero;
        private static bool IsInitialized = false;

        public static void Initialize(ISigScanner sigScanner)
        {
            if (IsInitialized) return;
            try
            {
                var funcAddr = sigScanner.ScanText(GetFilteredUtf8StringSig);
                if (funcAddr == IntPtr.Zero) return;

                GetFilteredFunc = Marshal.GetDelegateForFunctionPointer<GetFilteredUtf8StringDelegate>(funcAddr);

                var offsetAddr = sigScanner.ScanText(VulgarInstanceOffsetSig);
                if (offsetAddr == IntPtr.Zero) return;

                VulgarInstanceOffset = (IntPtr)Marshal.ReadInt32(offsetAddr + 3);
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "[CensorshipScanner] Init failed");
            }
        }

        public static string? GetFilteredString(string? input)
        {
            if (!IsInitialized || GetFilteredFunc == null || VulgarInstanceOffset == IntPtr.Zero)
            {
                return input;
            }

            if (string.IsNullOrEmpty(input))
                return input;

            try
            {
                var framework = Framework.Instance();
                if (framework == null)
                {
                    return input;
                }

                var vulgarInstance = Marshal.ReadIntPtr((IntPtr)framework + VulgarInstanceOffset);
                if (vulgarInstance == IntPtr.Zero)
                {
                    return input;
                }

                var utf8String = Utf8String.FromString(input);
                if (utf8String == null)
                {
                    return input;
                }

                try
                {
                    GetFilteredFunc.Invoke(vulgarInstance, utf8String);
                    return utf8String->ToString();
                }
                finally
                {
                    utf8String->Dtor(true);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "[CensorshipScanner] Exception during filtering process.");
                return input;
            }
        }

        public static bool ContainsCensoredWords(string? input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            var filtered = GetFilteredString(input);
            return filtered != input;
        }
    }
}