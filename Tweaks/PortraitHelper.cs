using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Game;
using Dalamud.Memory;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HaselTweaks.Enums.PortraitHelper;
using HaselTweaks.Extensions;
using HaselTweaks.Records.PortraitHelper;
using HaselTweaks.Sheets;
using HaselTweaks.Structs;
using HaselTweaks.Utils;
using HaselTweaks.Windows.PortraitHelperWindows;
using HaselTweaks.Windows.PortraitHelperWindows.Overlays;
using Lumina.Excel.GeneratedSheets;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static HaselTweaks.Structs.AgentBannerEditorState;
using RenderTargetManager = HaselTweaks.Structs.RenderTargetManager;

namespace HaselTweaks.Tweaks;

public partial class PortraitHelper : Tweak
{
    public override string Name => "Portrait Helper";
    public override string Description => "A helper for editing portraits.";

    public static Configuration Config => Plugin.Config.Tweaks.PortraitHelper;

    public class Configuration
    {
        [ConfigField(Type = ConfigFieldTypes.Ignore)]
        public List<SavedPreset> Presets = new();

        [ConfigField(Type = ConfigFieldTypes.Ignore)]
        public List<SavedPresetTag> PresetTags = new();

        [ConfigField(Type = ConfigFieldTypes.Ignore, Label = "Show Alignment Tool")]
        public bool ShowAlignmentTool = false;

        [ConfigField(Type = ConfigFieldTypes.Ignore, Label = "Vertical Lines"/*, DependsOn = nameof(ShowAlignmentTool), Min = 0, Max = 10*/)]
        public int AlignmentToolVerticalLines = 2;

        [ConfigField(Type = ConfigFieldTypes.Ignore, Label = "Vertical Color"/*, DependsOn = nameof(ShowAlignmentTool), Type = ConfigFieldTypes.Color4*/)]
        public Vector4 AlignmentToolVerticalColor = new(0, 0, 0, 1f);

        [ConfigField(Type = ConfigFieldTypes.Ignore, Label = "Horizontal Lines"/*, DependsOn = nameof(ShowAlignmentTool), Min = 0, Max = 10*/)]
        public int AlignmentToolHorizontalLines = 2;

        [ConfigField(Type = ConfigFieldTypes.Ignore, Label = "Horizontal Color"/*, DependsOn = nameof(ShowAlignmentTool), Type = ConfigFieldTypes.Color4*/)]
        public Vector4 AlignmentToolHorizontalColor = new(0, 0, 0, 1f);

        [ConfigField(Label = "Auto-update Portrait when Gearset was updated")]
        public bool AutoUpdatePortraitOnGearsetUpdate = true;

        public string GetPortraitThumbnailPath(string hash)
        {
            var portraitsPath = Path.Join(Service.PluginInterface.ConfigDirectory.FullName, "Portraits");

            if (!Directory.Exists(portraitsPath))
                Directory.CreateDirectory(portraitsPath);

            return Path.Join(portraitsPath, $"{hash}.png");
        }
    }

    public unsafe AgentBannerEditor* AgentBannerEditor;
    public unsafe AddonBannerEditor* AddonBannerEditor;

    private bool isOpen;
    private MenuBar? menuBar;
    private AdvancedImportOverlay? advancedImportOverlay;
    private AdvancedEditOverlay? advancedEditOverlay;
    private PresetBrowserOverlay? presetBrowserOverlay;
    private AlignmentToolSettingsOverlay? alignmentToolSettingsOverlay;
    private DateTime lastClipboardCheck = default;
    private uint lastClipboardSequenceNumber;

    public ViewMode OverlayViewMode { get; private set; } = ViewMode.Normal;
    public ImportFlags CurrentImportFlags { get; set; } = ImportFlags.All;

    public PortraitPreset? ClipboardPreset { get; private set; }

    public override unsafe void Enable()
    {
        if (GetAddon(AgentId.BannerEditor, out var addon))
            OnAddonOpen("BannerEditor", addon);
    }

    public override void Disable()
    {
        CloseWindows();
    }

    public override unsafe void OnAddonOpen(string addonName, AtkUnitBase* unitbase)
    {
        if (addonName == "BannerEditor")
        {
            GetAgent(AgentId.BannerEditor, out AgentBannerEditor);
            AddonBannerEditor = (AddonBannerEditor*)unitbase;

            Plugin.WindowSystem.AddWindow(menuBar = new(this));

            isOpen = true;
            return;
        }

        if (addonName == "Character" || addonName == "GearSetList")
        {
            var glamourData = GlamourData.Instance();

            // make sure Glamour Plates are loaded
            if (!glamourData->GlamourPlatesRequested || !glamourData->GlamourPlatesLoaded)
                glamourData->RequestGlamourPlates();

            return;
        }
    }

    public override unsafe void OnAddonClose(string addonName, AtkUnitBase* unitbase)
    {
        if (addonName != "BannerEditor")
            return;

        CloseWindows();

        isOpen = false;
    }

    private void CloseWindows()
    {
        if (menuBar != null)
        {
            Plugin.WindowSystem.RemoveWindow(menuBar);
            menuBar.Dispose();
            menuBar = null;
        }

        if (advancedImportOverlay != null)
        {
            Plugin.WindowSystem.RemoveWindow(advancedImportOverlay);
            advancedImportOverlay.IsOpen = false;
            advancedImportOverlay.OnClose();
            advancedImportOverlay = null;
        }

        if (advancedEditOverlay != null)
        {
            Plugin.WindowSystem.RemoveWindow(advancedEditOverlay);
            advancedEditOverlay.IsOpen = false;
            advancedEditOverlay.OnClose();
            advancedEditOverlay = null;
        }

        if (presetBrowserOverlay != null)
        {
            Plugin.WindowSystem.RemoveWindow(presetBrowserOverlay);
            presetBrowserOverlay.IsOpen = false;
            presetBrowserOverlay.OnClose();
            presetBrowserOverlay.Dispose();
            presetBrowserOverlay = null;
        }

        if (alignmentToolSettingsOverlay != null)
        {
            Plugin.WindowSystem.RemoveWindow(alignmentToolSettingsOverlay);
            alignmentToolSettingsOverlay.IsOpen = false;
            alignmentToolSettingsOverlay.OnClose();
            alignmentToolSettingsOverlay = null;
        }

        OverlayViewMode = ViewMode.Normal;
    }

    public override void OnFrameworkUpdate(Framework framework)
    {
        if (!isOpen)
            return;

        CheckClipboard();
    }

    public void ChangeView(ViewMode viewMode)
    {
        if (OverlayViewMode == viewMode)
            return;

        // open AdvancedImport
        if (viewMode == ViewMode.AdvancedImport && OverlayViewMode != ViewMode.AdvancedImport)
        {
            Plugin.WindowSystem.AddWindow(advancedImportOverlay = new(this));
        }

        // close AdvancedImport
        else if (viewMode != ViewMode.AdvancedImport && OverlayViewMode == ViewMode.AdvancedImport && advancedImportOverlay != null)
        {
            Plugin.WindowSystem.RemoveWindow(advancedImportOverlay);
            advancedImportOverlay.IsOpen = false;
            advancedImportOverlay.OnClose();
            advancedImportOverlay = null;
        }

        // open AdvancedEdit
        if (viewMode == ViewMode.AdvancedEdit && OverlayViewMode != ViewMode.AdvancedEdit)
        {
            Plugin.WindowSystem.AddWindow(advancedEditOverlay = new(this));
        }
        // close AdvancedEdit
        else if (viewMode != ViewMode.AdvancedEdit && OverlayViewMode == ViewMode.AdvancedEdit && advancedEditOverlay != null)
        {
            Plugin.WindowSystem.RemoveWindow(advancedEditOverlay);
            advancedEditOverlay.IsOpen = false;
            advancedEditOverlay.OnClose();
            advancedEditOverlay = null;
        }

        // open PresetBrowser
        if (viewMode == ViewMode.PresetBrowser && OverlayViewMode != ViewMode.PresetBrowser)
        {
            Plugin.WindowSystem.AddWindow(presetBrowserOverlay = new(this));
        }
        // close PresetBrowser
        else if (viewMode != ViewMode.PresetBrowser && OverlayViewMode == ViewMode.PresetBrowser && presetBrowserOverlay != null)
        {
            Plugin.WindowSystem.RemoveWindow(presetBrowserOverlay);
            presetBrowserOverlay.IsOpen = false;
            presetBrowserOverlay.OnClose();
            presetBrowserOverlay.Dispose();
            presetBrowserOverlay = null;
        }

        // open AlignmentToolSettings
        if (viewMode == ViewMode.AlignmentToolSettings && OverlayViewMode != ViewMode.AlignmentToolSettings)
        {
            Plugin.WindowSystem.AddWindow(alignmentToolSettingsOverlay = new(this));
        }
        // close AlignmentToolSettings
        else if (viewMode != ViewMode.AlignmentToolSettings && OverlayViewMode == ViewMode.AlignmentToolSettings && alignmentToolSettingsOverlay != null)
        {
            Plugin.WindowSystem.RemoveWindow(alignmentToolSettingsOverlay);
            alignmentToolSettingsOverlay.IsOpen = false;
            alignmentToolSettingsOverlay.OnClose();
            alignmentToolSettingsOverlay = null;
        }

        OverlayViewMode = viewMode;
    }

    public void CheckClipboard()
    {
        if (DateTime.Now - lastClipboardCheck <= TimeSpan.FromMilliseconds(100))
            return;

        if (!ClipboardUtils.IsClipboardFormatAvailable(ClipboardUtils.ClipboardFormat.CF_TEXT))
            return;

        var clipboardSequenceNumber = ClipboardUtils.GetClipboardSequenceNumber();

        if (lastClipboardSequenceNumber == clipboardSequenceNumber)
            return;

        if (!ClipboardUtils.OpenClipboard(0))
            return;

        lastClipboardSequenceNumber = clipboardSequenceNumber;

        var data = ClipboardUtils.GetClipboardData(ClipboardUtils.ClipboardFormat.CF_TEXT);
        if (data != 0)
        {
            var clipboardText = MemoryHelper.ReadString(data, 1024);
            ClipboardPreset = PortraitPreset.FromExportedString(clipboardText);

            if (ClipboardPreset != null)
                Debug($"Parsed ClipboardPreset: {ClipboardPreset}");
        }

        ClipboardUtils.CloseClipboard();

        lastClipboardCheck = DateTime.Now;
    }

    public async void PresetToClipboard(PortraitPreset? preset)
    {
        if (preset == null)
            return;

        await ClipboardUtils.OpenClipboard();

        if (!ClipboardUtils.EmptyClipboard())
            return;

        var clipboardText = Marshal.StringToHGlobalAnsi(preset.ToExportedString());
        if (ClipboardUtils.SetClipboardData(ClipboardUtils.ClipboardFormat.CF_TEXT, clipboardText) != 0)
            ClipboardPreset = preset;

        ClipboardUtils.CloseClipboard();
    }

    public unsafe Image<Bgra32>? GetCurrentCharaViewImage()
    {
        if (!GetAgent<AgentBannerEditor>(AgentId.BannerEditor, out var agentBannerEditor))
            return null;

        var charaViewTexture = RenderTargetManager.Instance()->GetCharaViewTexture(agentBannerEditor->EditorState->CharaView->Base.ClientObjectIndex);
        if (charaViewTexture == null || charaViewTexture->D3D11Texture2D == null)
            return null;

        var device = Service.PluginInterface.UiBuilder.Device;
        var texture = CppObject.FromPointer<Texture2D>((nint)charaViewTexture->D3D11Texture2D);

        // thanks to ChatGPT
        // Get the texture description
        var desc = texture.Description;

        // Create a staging texture with the same description
        using var stagingTexture = new Texture2D(device, new Texture2DDescription()
        {
            ArraySize = 1,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            Format = desc.Format,
            Height = desc.Height,
            Width = desc.Width,
            MipLevels = 1,
            OptionFlags = desc.OptionFlags,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging
        });

        // Copy the texture data to the staging texture
        device.ImmediateContext.CopyResource(texture, stagingTexture);

        // Map the staging texture
        device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out var dataStream);

        using var pixelDataStream = new MemoryStream();
        dataStream.CopyTo(pixelDataStream);

        // Unmap the staging texture
        device.ImmediateContext.UnmapSubresource(stagingTexture, 0);

        return Image.LoadPixelData<Bgra32>(pixelDataStream.ToArray(), desc.Width, desc.Height);
    }

    public unsafe PortraitPreset? StateToPreset()
    {
        if (!GetAgent<AgentBannerEditor>(AgentId.BannerEditor, out var agentBannerEditor))
            return null;

        var state = agentBannerEditor->EditorState;
        var preset = new PortraitPreset();

        var portraitData = (ExportedPortraitData*)IMemorySpace.GetDefaultSpace()->Malloc<ExportedPortraitData>();
        state->CharaView->ExportPortraitData(portraitData);
        preset.ReadExportedPortraitData(portraitData);
        IMemorySpace.Free(portraitData);

        preset.BannerFrame = state->BannerEntry.BannerFrame;
        preset.BannerDecoration = state->BannerEntry.BannerDecoration;

        return preset;
    }

    public unsafe void PresetToState(PortraitPreset? preset, ImportFlags importFlags)
    {
        if (preset == null)
            return;

        if (!GetAgent<AgentBannerEditor>(AgentId.BannerEditor, out var agentBannerEditor))
            return;

        if (!GetAddon<AddonBannerEditor>((AgentInterface*)agentBannerEditor, out var addonBannerEditor))
            return;

        Debug($"Importing Preset {preset.ToExportedString()} with ImportFlags {importFlags}");

        var state = agentBannerEditor->EditorState;
        var bannerEntry = state->BannerEntry;

        // read current portrait and then overwrite what the flags allow below
        var tempPortraitData = (ExportedPortraitData*)IMemorySpace.GetDefaultSpace()->Malloc<ExportedPortraitData>();
        state->CharaView->ExportPortraitData(tempPortraitData);

        var hasBgChanged =
            importFlags.HasFlag(ImportFlags.BannerBg) &&
            tempPortraitData->BannerBg != preset.BannerBg;

        var hasFrameChanged =
            importFlags.HasFlag(ImportFlags.BannerFrame) &&
            bannerEntry.BannerFrame != preset.BannerFrame;

        var hasDecorationChanged =
            importFlags.HasFlag(ImportFlags.BannerDecoration) &&
            bannerEntry.BannerDecoration != preset.BannerDecoration;

        var hasBannerTimelineChanged =
            importFlags.HasFlag(ImportFlags.BannerTimeline) &&
            tempPortraitData->BannerTimeline != preset.BannerTimeline;

        var hasExpressionChanged =
            importFlags.HasFlag(ImportFlags.Expression) &&
            tempPortraitData->Expression != preset.Expression;

        var hasAmbientLightingBrightnessChanged =
            importFlags.HasFlag(ImportFlags.AmbientLightingBrightness) &&
            tempPortraitData->AmbientLightingBrightness != preset.AmbientLightingBrightness;

        var hasAmbientLightingColorChanged =
            importFlags.HasFlag(ImportFlags.AmbientLightingColor) && (
                tempPortraitData->AmbientLightingColorRed != preset.AmbientLightingColorRed ||
                tempPortraitData->AmbientLightingColorGreen != preset.AmbientLightingColorGreen ||
                tempPortraitData->AmbientLightingColorBlue != preset.AmbientLightingColorBlue
            );

        var hasDirectionalLightingBrightnessChanged =
            importFlags.HasFlag(ImportFlags.DirectionalLightingBrightness) &&
            tempPortraitData->DirectionalLightingBrightness != preset.DirectionalLightingBrightness;

        var hasDirectionalLightingColorChanged =
            importFlags.HasFlag(ImportFlags.DirectionalLightingColor) && (
                tempPortraitData->DirectionalLightingColorRed != preset.DirectionalLightingColorRed ||
                tempPortraitData->DirectionalLightingColorGreen != preset.DirectionalLightingColorGreen ||
                tempPortraitData->DirectionalLightingColorBlue != preset.DirectionalLightingColorBlue
            );

        var hasDirectionalLightingVerticalAngleChanged =
            importFlags.HasFlag(ImportFlags.DirectionalLightingVerticalAngle) &&
            tempPortraitData->DirectionalLightingVerticalAngle != preset.DirectionalLightingVerticalAngle;

        var hasDirectionalLightingHorizontalAngleChanged =
            importFlags.HasFlag(ImportFlags.DirectionalLightingHorizontalAngle) &&
            tempPortraitData->DirectionalLightingHorizontalAngle != preset.DirectionalLightingHorizontalAngle;

        var hasAnimationProgressChanged =
            importFlags.HasFlag(ImportFlags.AnimationProgress) &&
            !tempPortraitData->AnimationProgress.IsApproximately(preset.AnimationProgress, 0.01f);

        var hasCameraPositionChanged =
            importFlags.HasFlag(ImportFlags.CameraPosition) &&
            !tempPortraitData->CameraPosition.IsApproximately(preset.CameraPosition);

        var hasCameraTargetChanged =
            importFlags.HasFlag(ImportFlags.CameraTarget) &&
            !tempPortraitData->CameraTarget.IsApproximately(preset.CameraTarget);

        var hasHeadDirectionChanged =
            importFlags.HasFlag(ImportFlags.HeadDirection) &&
            !tempPortraitData->HeadDirection.IsApproximately(preset.HeadDirection);

        var hasEyeDirectionChanged =
            importFlags.HasFlag(ImportFlags.EyeDirection) &&
            !tempPortraitData->EyeDirection.IsApproximately(preset.EyeDirection);

        var hasCameraZoomChanged =
            importFlags.HasFlag(ImportFlags.CameraZoom) &&
            tempPortraitData->CameraZoom != preset.CameraZoom;

        var hasImageRotationChanged =
            importFlags.HasFlag(ImportFlags.ImageRotation) &&
            tempPortraitData->ImageRotation != preset.ImageRotation;

        if (hasBgChanged)
        {
            Debug($"- BannerBg changed from {tempPortraitData->BannerBg} to {preset.BannerBg}");

            bannerEntry.BannerBg = preset.BannerBg;
            tempPortraitData->BannerBg = preset.BannerBg;

            addonBannerEditor->BackgroundDropdown->SetValue(GetListIndex(state->BackgroundItems, state->BackgroundItemsCount, preset.BannerBg));
        }

        if (hasFrameChanged)
        {
            Debug($"- BannerFrame changed from {bannerEntry.BannerFrame} to {preset.BannerFrame}");

            state->SetFrame(preset.BannerFrame);

            addonBannerEditor->FrameDropdown->SetValue(GetListIndex(state->FrameItems, state->FrameItemsCount, preset.BannerFrame));
        }

        if (hasDecorationChanged)
        {
            Debug($"- BannerDecoration changed from {bannerEntry.BannerDecoration} to {preset.BannerDecoration}");

            state->SetAccent(preset.BannerDecoration);

            addonBannerEditor->AccentDropdown->SetValue(GetListIndex(state->AccentItems, state->AccentItemsCount, preset.BannerDecoration));
        }

        if (hasBgChanged || hasFrameChanged || hasDecorationChanged)
        {
            Debug("- Preset changed");

            var presetIndex = state->GetPresetIndex(bannerEntry.BannerBg, bannerEntry.BannerFrame, bannerEntry.BannerDecoration);
            if (presetIndex < 0)
            {
                presetIndex = addonBannerEditor->NumPresets - 1;

                addonBannerEditor->PresetDropdown->List->SetListLength(addonBannerEditor->NumPresets); // increase to maximum, so "Custom" is displayed
            }

            addonBannerEditor->PresetDropdown->SetValue(presetIndex);
        }

        if (hasBannerTimelineChanged)
        {
            Debug($"- BannerTimeline changed from {tempPortraitData->BannerTimeline} to {preset.BannerTimeline}");

            bannerEntry.BannerTimeline = preset.BannerTimeline;
            tempPortraitData->BannerTimeline = preset.BannerTimeline;

            addonBannerEditor->PoseDropdown->SetValue(GetListIndex(state->BannerTimelineItems, state->BannerTimelineItemsCount, preset.BannerTimeline));
        }

        if (hasExpressionChanged)
        {
            Debug($"- Expression changed from {tempPortraitData->Expression} to {preset.Expression}");

            bannerEntry.Expression = preset.Expression;
            tempPortraitData->Expression = preset.Expression;

            addonBannerEditor->ExpressionDropdown->SetValue(GetExpressionListIndex(state->ExpressionItems, state->ExpressionItemsCount, preset.Expression));
        }

        if (hasAmbientLightingBrightnessChanged)
        {
            Debug($"- AmbientLightingBrightness changed from {tempPortraitData->AmbientLightingBrightness} to {preset.AmbientLightingBrightness}");

            tempPortraitData->AmbientLightingBrightness = preset.AmbientLightingBrightness;

            addonBannerEditor->AmbientLightingBrightnessSlider->SetValue(preset.AmbientLightingBrightness);
        }

        if (hasAmbientLightingColorChanged)
        {
            Debug($"- AmbientLightingColor changed from {tempPortraitData->AmbientLightingColorRed}, {tempPortraitData->AmbientLightingColorGreen}, {tempPortraitData->AmbientLightingColorBlue} to {preset.AmbientLightingColorRed}, {preset.AmbientLightingColorGreen}, {preset.AmbientLightingColorBlue}");

            tempPortraitData->AmbientLightingColorRed = preset.AmbientLightingColorRed;
            tempPortraitData->AmbientLightingColorGreen = preset.AmbientLightingColorGreen;
            tempPortraitData->AmbientLightingColorBlue = preset.AmbientLightingColorBlue;

            addonBannerEditor->AmbientLightingColorRedSlider->SetValue(preset.AmbientLightingColorRed);
            addonBannerEditor->AmbientLightingColorGreenSlider->SetValue(preset.AmbientLightingColorGreen);
            addonBannerEditor->AmbientLightingColorBlueSlider->SetValue(preset.AmbientLightingColorBlue);
        }

        if (hasDirectionalLightingBrightnessChanged)
        {
            Debug($"- DirectionalLightingBrightness changed from {tempPortraitData->DirectionalLightingBrightness} to {preset.DirectionalLightingBrightness}");

            tempPortraitData->DirectionalLightingBrightness = preset.DirectionalLightingBrightness;

            addonBannerEditor->DirectionalLightingBrightnessSlider->SetValue(preset.DirectionalLightingBrightness);
        }

        if (hasDirectionalLightingColorChanged)
        {
            Debug($"- DirectionalLightingColor changed from {tempPortraitData->DirectionalLightingColorRed}, {tempPortraitData->DirectionalLightingColorGreen}, {tempPortraitData->DirectionalLightingColorBlue} to {preset.DirectionalLightingColorRed}, {preset.DirectionalLightingColorGreen}, {preset.DirectionalLightingColorBlue}");

            tempPortraitData->DirectionalLightingColorRed = preset.DirectionalLightingColorRed;
            tempPortraitData->DirectionalLightingColorGreen = preset.DirectionalLightingColorGreen;
            tempPortraitData->DirectionalLightingColorBlue = preset.DirectionalLightingColorBlue;

            addonBannerEditor->DirectionalLightingColorRedSlider->SetValue(preset.DirectionalLightingColorRed);
            addonBannerEditor->DirectionalLightingColorGreenSlider->SetValue(preset.DirectionalLightingColorGreen);
            addonBannerEditor->DirectionalLightingColorBlueSlider->SetValue(preset.DirectionalLightingColorBlue);
        }

        if (hasDirectionalLightingVerticalAngleChanged)
        {
            Debug($"- DirectionalLightingVerticalAngle changed from {tempPortraitData->DirectionalLightingVerticalAngle} to {preset.DirectionalLightingVerticalAngle}");

            tempPortraitData->DirectionalLightingVerticalAngle = preset.DirectionalLightingVerticalAngle;

            addonBannerEditor->DirectionalLightingVerticalAngleSlider->SetValue(preset.DirectionalLightingVerticalAngle);
        }

        if (hasDirectionalLightingHorizontalAngleChanged)
        {
            Debug($"- DirectionalLightingHorizontalAngle changed from {tempPortraitData->DirectionalLightingHorizontalAngle} to {preset.DirectionalLightingHorizontalAngle}");

            tempPortraitData->DirectionalLightingHorizontalAngle = preset.DirectionalLightingHorizontalAngle;

            addonBannerEditor->DirectionalLightingHorizontalAngleSlider->SetValue(preset.DirectionalLightingHorizontalAngle);
        }

        if (hasAnimationProgressChanged)
        {
            Debug($"- AnimationProgress changed from {tempPortraitData->AnimationProgress} to {preset.AnimationProgress}");

            tempPortraitData->AnimationProgress = preset.AnimationProgress;
        }

        if (hasCameraPositionChanged)
        {
            Debug($"- CameraPosition changed from {tempPortraitData->CameraPosition.X}, {tempPortraitData->CameraPosition.Y}, {tempPortraitData->CameraPosition.Z}, {tempPortraitData->CameraPosition.W} to {preset.CameraPosition.X}, {preset.CameraPosition.Y}, {preset.CameraPosition.Z}, {preset.CameraPosition.W}");

            tempPortraitData->CameraPosition = preset.CameraPosition;
        }

        if (hasCameraTargetChanged)
        {
            Debug($"- CameraTarget changed from {tempPortraitData->CameraTarget.X}, {tempPortraitData->CameraTarget.Y}, {tempPortraitData->CameraTarget.Z}, {tempPortraitData->CameraTarget.W} to {preset.CameraTarget.X}, {preset.CameraTarget.Y}, {preset.CameraTarget.Z}, {preset.CameraTarget.W}");

            tempPortraitData->CameraTarget = preset.CameraTarget;
        }

        if (hasHeadDirectionChanged)
        {
            Debug($"- HeadDirection changed from {tempPortraitData->HeadDirection.X}, {tempPortraitData->HeadDirection.Y} to {preset.HeadDirection.X}, {preset.HeadDirection.Y}");

            tempPortraitData->HeadDirection = preset.HeadDirection;
        }

        if (hasEyeDirectionChanged)
        {
            Debug($"- EyeDirection changed from {tempPortraitData->EyeDirection.X}, {tempPortraitData->EyeDirection.Y} to {preset.EyeDirection.X}, {preset.EyeDirection.Y}");

            tempPortraitData->EyeDirection = preset.EyeDirection;
        }

        if (hasCameraZoomChanged)
        {
            Debug($"- CameraZoom changed from {tempPortraitData->CameraZoom} to {preset.CameraZoom}");

            tempPortraitData->CameraZoom = preset.CameraZoom;

            addonBannerEditor->CameraZoomSlider->SetValue(preset.CameraZoom);
        }

        if (hasImageRotationChanged)
        {
            Debug($"- ImageRotation changed from {tempPortraitData->ImageRotation} to {preset.ImageRotation}");

            tempPortraitData->ImageRotation = preset.ImageRotation;

            addonBannerEditor->ImageRotation->SetValue(preset.ImageRotation);
        }

        state->CharaView->ImportPortraitData(tempPortraitData);

        addonBannerEditor->PlayAnimationCheckbox->SetValue(false);
        addonBannerEditor->HeadFacingCameraCheckbox->SetValue(false);
        addonBannerEditor->EyesFacingCameraCheckbox->SetValue(false);

        state->SetHasChanged(
            state->HasDataChanged ||
            hasBgChanged ||
            hasFrameChanged ||
            hasDecorationChanged ||
            hasBannerTimelineChanged ||
            hasExpressionChanged ||
            hasAmbientLightingBrightnessChanged ||
            hasAmbientLightingColorChanged ||
            hasDirectionalLightingBrightnessChanged ||
            hasDirectionalLightingColorChanged ||
            hasDirectionalLightingVerticalAngleChanged ||
            hasDirectionalLightingHorizontalAngleChanged ||
            hasAnimationProgressChanged ||
            hasCameraPositionChanged ||
            hasCameraTargetChanged ||
            hasHeadDirectionChanged ||
            hasEyeDirectionChanged ||
            hasCameraZoomChanged ||
            hasImageRotationChanged
        );

        IMemorySpace.Free(tempPortraitData);

        Debug("Import complete");
    }

    private static unsafe int GetListIndex(GenericDropdownItem** items, uint itemCount, ushort id)
    {
        for (var i = 0; i < itemCount; i++)
        {
            var entry = items[i];
            if (entry->Id == id && entry->Data != 0)
            {
                return i;
            }
        }

        return 0;
    }

    private static unsafe int GetExpressionListIndex(ExpressionDropdownItem** items, uint itemCount, ushort id)
    {
        for (var i = 0; i < itemCount; i++)
        {
            var entry = items[i];
            if (entry->Id == id && entry->Data != 0)
            {
                return i;
            }
        }

        return 0;
    }

    [SigHook("E8 ?? ?? ?? ?? 8B E8 83 F8 FE 0F 8E ?? ?? ?? ?? 80 BE ?? ?? ?? ?? ??")]
    public unsafe int RaptureGearsetModule_GearsetUpdate(RaptureGearsetModule* raptureGearsetModule, int gearsetIndex)
    {
        var ret = RaptureGearsetModule_GearsetUpdateHook.Original(raptureGearsetModule, gearsetIndex);

        if (!Config.AutoUpdatePortraitOnGearsetUpdate)
            return ret;

        var gearset = raptureGearsetModule->GetGearset(gearsetIndex);
        if (gearset == null)
            return ret;

        var bannerIndex = *(byte*)((nint)gearset + 0x36);
        if (bannerIndex == 0) // no banner linked
            return ret;

        var bannerModule = BannerModule.Instance();
        var bannerId = bannerModule->GetBannerIdByBannerIndex(bannerIndex - 1);
        if (bannerId < 0) // banner not found
            return ret;

        var banner = bannerModule->GetBannerById(bannerId);
        if (banner == null) // banner not found
            return ret;

        var gearsetClassJobCategoryRowId = Service.Data.GetExcelSheet<ClassJob>()?.GetRow(gearset->ClassJob)?.ClassJobCategory.Row ?? 0;
        var gearsetClassJobCategory = Service.Data.GetExcelSheet<ExtendedClassJobCategory>()?.GetRow(gearsetClassJobCategoryRowId);
        if (gearsetClassJobCategory == null)
            return ret;

        var bannerTimelineClassJobCategory = Service.Data.GetExcelSheet<ExtendedClassJobCategory>()?.GetRow(banner->BannerTimelineClassJobCategory);
        if (bannerTimelineClassJobCategory == null)
            return ret;

        if (gearsetClassJobCategory[gearset->ClassJob] != bannerTimelineClassJobCategory[gearset->ClassJob])
        {
            Warning("Can't update Portrait because ClassJob classification in ClassJobCategory doesn't match (" +
                $"ClassJob: {gearset->ClassJob}, " +
                $"GearSetClassJobCategory: {gearsetClassJobCategoryRowId}, " +
                $"BannerTimelineClassJobCategory: {banner->BannerTimelineClassJobCategory}" +
                ")");
            return ret;
        }

        Log($"Gearset #{gearsetIndex + 1} updated - updating Portrait");

        const int ItemCount = 14;

        var ptr = Marshal.AllocHGlobal(ItemCount * 4 + ItemCount);
        var items = new Span<RaptureGearsetModule.GearsetItem>(gearset->ItemsData, ItemCount);
        var glamourData = GlamourData.Instance();

        for (var i = 0; i < ItemCount; i++)
        {
            var item = items[i];
            var itemId = item.ItemID;
            var stainId = item.Stain;

            if (item.GlamourId != 0)
                itemId = item.GlamourId;

            if (glamourData->GlamourPlatesLoaded && gearset->GlamourSetLink > 0)
            {
                var glamourPlate = glamourData->GlamourPlatesSpan[gearset->GlamourSetLink - 1];

                var itemIndex = i; // only 12 allowed here, so we have to filter:
                if (itemIndex > 4) // ignore belt slot
                    itemIndex -= 1;
                if (itemIndex < 12) // cut off soulstone
                {
                    var glamourItemId = glamourPlate.ItemIds[itemIndex];
                    if (glamourItemId != 0)
                    {
                        itemId = glamourItemId;
                        stainId = glamourPlate.StainIds[itemIndex];
                    }
                }
            }

            *(uint*)(ptr + i * 4) = itemId;
            *(byte*)(ptr + ItemCount * 4 + i) = stainId;
        }

        var gearVisibilityFlag = BannerGearVisibilityFlag.None;

        if (!gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.WeaponsVisible))
            gearVisibilityFlag |= BannerGearVisibilityFlag.WeaponHidden;

        if (!gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.HeadgearVisible))
            gearVisibilityFlag |= BannerGearVisibilityFlag.HeadgearHidden;

        if (gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.VisorEnabled))
            gearVisibilityFlag |= BannerGearVisibilityFlag.VisorClosed;

        var gearsetHash = GenerateGearsetHash((uint*)ptr, (byte*)(ptr + ItemCount * 4), gearVisibilityFlag);

        Marshal.FreeHGlobal(ptr);

        banner->GearsetHash = gearsetHash;
        banner->LastUpdated = (uint)DateTimeOffset.Now.ToUnixTimeSeconds();

        bannerModule->UserFileEvent.IsSavePending = true;

        return ret;
    }

    [Signature("E8 ?? ?? ?? ?? 89 43 48 48 83 C4 20")]
    public readonly GenerateGearsetHashDelegate GenerateGearsetHash = null!;
    public unsafe delegate uint GenerateGearsetHashDelegate(uint* itemIds, byte* stainIds, BannerGearVisibilityFlag gearVisibilityFlag);
}
