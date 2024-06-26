using FFXIVClientStructs.FFXIV.Component.GUI;

namespace HaselTweaks.Structs;

// ctor "48 89 5C 24 ?? 57 48 83 EC 20 48 8B D9 E8 ?? ?? ?? ?? C6 83 ?? ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 03 48 8D 8B ?? ?? ?? ?? 33 FF 48 89 BB ?? ?? ?? ?? 48 89 BB ?? ?? ?? ?? 48 89 BB ?? ?? ?? ?? 48 89 BB ?? ?? ?? ?? 48 89 BB ?? ?? ?? ?? 48 89 BB"
// aka RetainerInventory
[StructLayout(LayoutKind.Explicit, Size = 0x2F0)]
public partial struct AddonInventoryRetainer
{
    public const int NUM_TABS = 6;

    [FieldOffset(0)] public AtkUnitBase AtkUnitBase;

    [FieldOffset(0x2E8)] public int TabIndex;

    // called via RetainerInventory vf68
    [MemberFunction("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 70 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 48 8B F1 48 8B 89")]
    public readonly partial void SetTab(int tab);
}
