using FFXIVClientStructs.FFXIV.Client.System.String;

namespace HaselTweaks.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x3488)]
public unsafe partial struct HaselRaptureLogModule
{
    [FieldOffset(0xF8)] public HaselRaptureTextModule* RaptureTextModule;

    [FieldOffset(0x100)] public HaselAtkFontCodeModule* AtkFontCodeModule;

    [FixedSizeArray<Utf8String>(10)]
    [FieldOffset(0x108)] public fixed byte TempParseMessage[0x68 * 10];

    [FieldOffset(0x520)] public nint LogKindSheet;

    [MemberFunction("E8 ?? ?? ?? ?? 89 43 28 41 FF CD")]
    public readonly unsafe partial uint FormatLogMessage(uint logKindId, Utf8String* sender, Utf8String* message, int* timestamp, nint a6, Utf8String* a7, int chatTabIndex);
}
