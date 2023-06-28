using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.System.String;

namespace HaselTweaks.Utils;

public unsafe class DisposableUtf8String : DisposableCreatable<Utf8String>, IDisposable
{
    public DisposableUtf8String() : base()
    {
    }

    public DisposableUtf8String(byte* text) : base()
    {
        SetString(text);
    }

    public DisposableUtf8String(byte[] text) : base()
    {
        SetString(text);
    }

    public DisposableUtf8String(string text) : base()
    {
        SetString(text);
    }

    public DisposableUtf8String(SeString text) : base()
    {
        SetString(text);
    }

    public void SetString(byte* text)
        => Ptr->SetString(text);

    public void SetString(byte[] text)
        => Ptr->SetString(text);

    public void SetString(string text)
        => Ptr->SetString(text);

    public void SetString(SeString text)
        => SetString(text.Encode());

    public new string ToString()
        => Ptr->ToString();

    public SeString ToSeString()
        => SeString.Parse(Ptr->StringPtr, (int)Ptr->BufSize);

    public new void Dispose()
    {
        Ptr->Dtor();
        base.Dispose();
    }
}
