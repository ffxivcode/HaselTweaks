using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud;
using Dalamud.Interface;
using Dalamud.Interface.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using HaselTweaks.Interfaces;
using HaselTweaks.Records;
using HaselTweaks.Utils;
using ImGuiNET;
using ImGuiScene;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Svg;

namespace HaselTweaks.Windows;

public partial class PluginWindow : Window, IDisposable
{
    private const uint SidebarWidth = 250;
    private const uint ConfigWidth = SidebarWidth * 2;
    private const string LogoManifestResource = "HaselTweaks.Assets.Logo.svg";

    private string SelectedTweak = string.Empty;

    private bool IsLogoLoading;
    private TextureWrap? LogoTextureWrap;
    private readonly Point LogoSize = new(580, 180);
    private Point RenderedLogoSize = new(0, 0);

    public TextureManager? TextureManager { get; private set; }

    [GeneratedRegex("\\.0$")]
    private static partial Regex VersionPatchZeroRegex();

    public PluginWindow() : base("HaselTweaks")
    {
        var style = ImGui.GetStyle();
        var width = SidebarWidth + ConfigWidth + style.ItemSpacing.X + style.FramePadding.X * 2;

        Size = new Vector2(width, 600);
        SizeConstraints = new()
        {
            MinimumSize = new Vector2(width, 600),
            MaximumSize = new Vector2(4096, 2160)
        };

        SizeCondition = ImGuiCond.Appearing;

        Flags |= ImGuiWindowFlags.AlwaysAutoResize;
        Flags |= ImGuiWindowFlags.NoSavedSettings;

        UpdateLogo();
    }

    public void Dispose()
    {
        LogoTextureWrap?.Dispose();
        TextureManager?.Dispose();
        TextureManager = null;
    }

    public override void OnOpen()
    {
        TextureManager ??= new();
    }

    public override void Update()
    {
        UpdateLogo();
    }

    private void UpdateLogo()
    {
        if (IsLogoLoading)
            return;

        RenderedLogoSize.X = (int)(LogoSize.X * (LogoSize.X / ConfigWidth * 0.6f) * ImGui.GetIO().FontGlobalScale);
        RenderedLogoSize.Y = (int)(LogoSize.Y * (RenderedLogoSize.X / (float)LogoSize.X));

        if (RenderedLogoSize.X <= 0 || RenderedLogoSize.Y <= 0)
            return;

        if (LogoTextureWrap != null && LogoTextureWrap.Width == RenderedLogoSize.X && LogoTextureWrap.Height == RenderedLogoSize.Y)
            return;

        IsLogoLoading = true;

        Task.Run(() =>
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(LogoManifestResource);
                if (stream == null)
                {
                    PluginLog.Error($"ManifestResource {LogoManifestResource} not found");
                    return;
                }

                using var reader = new StreamReader(stream);

                var svgDocument = SvgDocument.FromSvg<SvgDocument>(reader.ReadToEnd());

                using var bitmap = svgDocument.Draw(RenderedLogoSize.X, RenderedLogoSize.Y);
                using var memoryStream = new MemoryStream();
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                memoryStream.Seek(0, SeekOrigin.Begin);
                var image = Image.Load<Rgba32>(memoryStream);

                var data = new byte[4 * image.Width * image.Height];
                image.CopyPixelDataTo(data);

                LogoTextureWrap?.Dispose();
                LogoTextureWrap = Service.PluginInterface.UiBuilder.LoadImageRaw(data, image.Width, image.Height, 4);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Error while loading logo");
            }
            finally
            {
                IsLogoLoading = false;
            }
        });
    }

    public override void OnClose()
    {
        SelectedTweak = string.Empty;

        TextureManager?.Dispose();
        TextureManager = null;

        foreach (var tweak in Plugin.Tweaks)
        {
            if (tweak.Enabled && tweak.HasCustomConfig)
            {
                tweak.OnConfigWindowClose();
            }
        }
    }

    public override void Draw()
    {
        DrawSidebar();
        ImGui.SameLine();
        DrawConfig();
    }

    private void DrawSidebar()
    {
        var scale = ImGui.GetIO().FontGlobalScale;
        using var child = ImRaii.Child("##HaselTweaks_Sidebar", new Vector2(SidebarWidth * scale, -1), true);
        if (!child || !child.Success)
            return;

        using var table = ImRaii.Table("##HaselTweaks_SidebarTable", 2, ImGuiTableFlags.NoSavedSettings);
        if (!table || !table.Success)
            return;

        ImGui.TableSetupColumn("Checkbox", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Tweak Name", ImGuiTableColumnFlags.WidthStretch);

        foreach (var tweak in Plugin.Tweaks.OrderBy(t => t.Name))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var enabled = tweak.Enabled;
            var fixY = false;

            if (!tweak.Ready || tweak.Outdated)
            {
                var startPos = ImGui.GetCursorPos();
                var drawList = ImGui.GetWindowDrawList();
                var pos = ImGui.GetWindowPos() + startPos - new Vector2(0, ImGui.GetScrollY());
                var frameHeight = ImGui.GetFrameHeight();

                var size = new Vector2(frameHeight);
                ImGui.SetCursorPos(startPos);
                ImGui.Dummy(size);

                if (ImGui.IsItemHovered())
                {
                    var (status, color) = GetTweakStatus(tweak);
                    using var tooltip = ImRaii.Tooltip();
                    if (tooltip != null && tooltip.Success)
                    {
                        ImGuiUtils.TextUnformattedColored(color, status);
                    }
                }

                drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ImGuiCol.FrameBg), 3f, ImDrawFlags.RoundCornersAll);

                var pad = frameHeight / 4f;
                pos += new Vector2(pad);
                size -= new Vector2(pad) * 2;

                drawList.PathLineTo(pos);
                drawList.PathLineTo(pos + size);
                drawList.PathStroke(ImGui.GetColorU32(ImGuiUtils.ColorRed), ImDrawFlags.None, frameHeight / 5f * 0.5f);

                drawList.PathLineTo(pos + new Vector2(0, size.Y));
                drawList.PathLineTo(pos + new Vector2(size.X, 0));
                drawList.PathStroke(ImGui.GetColorU32(ImGuiUtils.ColorRed), ImDrawFlags.None, frameHeight / 5f * 0.5f);

                fixY = true;
            }
            else
            {
                if (ImGui.Checkbox($"##Enabled_{tweak.InternalName}", ref enabled))
                {
                    if (!enabled)
                    {
                        tweak.DisableInternal();

                        if (Plugin.Config.EnabledTweaks.Contains(tweak.InternalName))
                        {
                            Plugin.Config.EnabledTweaks.Remove(tweak.InternalName);
                            Plugin.Config.Save();
                        }
                    }
                    else
                    {
                        tweak.EnableInternal();

                        if (!Plugin.Config.EnabledTweaks.Contains(tweak.InternalName))
                        {
                            Plugin.Config.EnabledTweaks.Add(tweak.InternalName);
                            Plugin.Config.Save();
                        }
                    }
                }
            }

            ImGui.TableNextColumn();

            if (fixY)
            {
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3); // if i only knew why this happens
            }

            if (!tweak.Ready || tweak.Outdated)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiUtils.ColorRed);
            }
            else if (!enabled)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiUtils.ColorGrey);
            }

            if (ImGui.Selectable($"{tweak.Name}##Selectable_{tweak.InternalName}", SelectedTweak == tweak.InternalName))
            {
                SelectedTweak = SelectedTweak != tweak.InternalName
                    ? tweak.InternalName
                    : string.Empty;
            }

            if (!tweak.Ready || tweak.Outdated || !enabled)
            {
                ImGui.PopStyleColor();
            }
        }
    }

    private void DrawConfig()
    {
        var scale = ImGui.GetIO().FontGlobalScale;
        using var child = ImRaii.Child("##HaselTweaks_Config", new Vector2(ConfigWidth * scale, -1), true);
        if (!child || !child.Success)
            return;

        if (string.IsNullOrEmpty(SelectedTweak))
        {
            var drawList = ImGui.GetWindowDrawList();
            var cursorPos = ImGui.GetCursorPos();
            var absolutePos = ImGui.GetWindowPos() + cursorPos;
            var contentAvail = ImGui.GetContentRegionAvail();
            var fontScale = ImGui.GetIO().FontGlobalScale;

            // I miss CSS...
            var offset = new Vector2(0, -8) * fontScale;
            var pluginNameSize = new Vector2(88, 18) * fontScale;
            var spacing = new Vector2(0, 28) * fontScale;

            if (!IsLogoLoading && LogoTextureWrap != null && LogoTextureWrap.ImGuiHandle != 0)
            {
                ImGui.SetCursorPos(contentAvail / 2 - RenderedLogoSize / 2);
                ImGui.Image(LogoTextureWrap.ImGuiHandle, RenderedLogoSize);
            }

            // links, bottom left
            ImGui.SetCursorPos(cursorPos + new Vector2(0, contentAvail.Y - ImGui.CalcTextSize(" ").Y));
            ImGuiUtils.DrawLink("GitHub", "Visit the HaselTweaks GitHub Repository", "https://github.com/Haselnussbomber/HaselTweaks");
            ImGuiUtils.BulletSeparator();
            ImGuiUtils.DrawLink("Ko-fi", "Support me on Ko-fi", "https://ko-fi.com/haselnussbomber");

            // version, bottom right
#if DEBUG
            ImGui.SetCursorPos(cursorPos + contentAvail - ImGui.CalcTextSize("dev"));
            ImGuiUtils.DrawLink("dev", "Compare changes", $"https://github.com/Haselnussbomber/HaselTweaks/compare/main...dev");
#else
            var version = GetType().Assembly.GetName().Version;
            if (version != null)
            {
                var versionString = "v" + VersionPatchZeroRegex().Replace(version.ToString(), "");
                ImGui.SetCursorPos(cursorPos + contentAvail - ImGui.CalcTextSize(versionString));
                ImGuiUtils.DrawLink(versionString, "Visit Release Notes", $"https://github.com/Haselnussbomber/HaselTweaks/releases/tag/{versionString}");
            }
#endif

            return;
        }

        var tweak = Plugin.Tweaks.FirstOrDefault(t => t.InternalName == SelectedTweak);
        if (tweak == null)
            return;

        ImGuiUtils.TextUnformattedColored(ImGuiUtils.ColorGold, tweak.Name);

        var (status, color) = GetTweakStatus(tweak);

        var posX = ImGui.GetCursorPosX();
        var windowX = ImGui.GetContentRegionAvail().X;
        var textSize = ImGui.CalcTextSize(status);

        ImGui.SameLine(windowX - textSize.X);

        ImGuiUtils.TextUnformattedColored(color, status);

        if (tweak.HasDescription)
        {
            ImGuiUtils.DrawPaddedSeparator();
            tweak.DrawDescription();
        }

        if (tweak.HasIncompatibilityWarning)
        {
            ImGuiUtils.DrawSection("Incompatibility Warning");
            tweak.DrawIncompatibilityWarning(this);
        }

#if DEBUG
        if (tweak.LastException != null)
        {
            ImGuiUtils.DrawSection("[DEBUG] Exception");
            ImGuiHelpers.SafeTextColoredWrapped(ImGuiUtils.ColorRed, tweak.LastException.Message.Replace("HaselTweaks.Tweaks.", ""));
            ImGuiHelpers.SafeTextColoredWrapped(ImGuiUtils.ColorGrey2, tweak.LastException.StackTrace ?? "");
        }
#endif

        if (tweak.HasCustomConfig)
        {
            ImGuiUtils.DrawSection("Configuration");
            tweak.DrawCustomConfig();
        }
        else
        {
            var config = Plugin.Config.Tweaks.GetType().GetProperty(tweak.InternalName)?.GetValue(Plugin.Config.Tweaks);
            if (config != null)
            {
                var configType = config.GetType();
                var configFields = configType.GetFields()
                    .Select(field =>
                    {
                        var attr = (ConfigFieldAttribute?)Attribute.GetCustomAttribute(field, typeof(ConfigFieldAttribute));
                        return (field, attr);
                    })
                    .Where((fa) => fa.attr?.Type != ConfigFieldTypes.Ignore);

                if (configFields.Any())
                {
                    ImGuiUtils.DrawSection("Configuration");

                    foreach (var (field, attr) in configFields)
                    {
                        var hasDependency = !string.IsNullOrEmpty(attr?.DependsOn);
                        var isDisabled = hasDependency && (bool?)configType.GetField(attr!.DependsOn)?.GetValue(config) == false;
                        var indent = hasDependency ? ImGuiUtils.ConfigIndent() : null;
                        var disabled = isDisabled ? ImRaii.Disabled() : null;

                        if (attr == null)
                        {
#if DEBUG
                            ImGuiUtils.TextUnformattedColored(ImGuiUtils.ColorRed, $"No ConfigFieldAttribute for {field.Name}");
#endif
                        }
                        else if (attr.Type == ConfigFieldTypes.Color4)
                        {
                            var data = Activator.CreateInstance(typeof(ConfigDrawData<>).MakeGenericType(new Type[] { field.FieldType }))!;

                            data.GetType().GetProperty("Tweak")!.SetValue(data, tweak);
                            data.GetType().GetProperty("Config")!.SetValue(data, config);
                            data.GetType().GetProperty("Field")!.SetValue(data, field);
                            data.GetType().GetProperty("Attr")!.SetValue(data, attr);

                            if (field.FieldType.Name == nameof(Vector4))
                            {
                                DrawColor4((ConfigDrawData<Vector4>)data);
                            }
                            else
                            {
                                DrawInvalidType(field);
                            }
                        }
                        else if (attr.Type == ConfigFieldTypes.Auto)
                        {
                            var data = Activator.CreateInstance(typeof(ConfigDrawData<>).MakeGenericType(new Type[] { field.FieldType }))!;

                            data.GetType().GetProperty("Tweak")!.SetValue(data, tweak);
                            data.GetType().GetProperty("Config")!.SetValue(data, config);
                            data.GetType().GetProperty("Field")!.SetValue(data, field);
                            data.GetType().GetProperty("Attr")!.SetValue(data, attr);

                            switch (field.FieldType.Name)
                            {
                                case nameof(String): DrawString((ConfigDrawData<string>)data); break;
                                case nameof(Single): DrawFloat((ConfigDrawData<float>)data); break;
                                case nameof(Int32): DrawInt((ConfigDrawData<int>)data); break;
                                case nameof(Boolean): DrawBool((ConfigDrawData<bool>)data); break;

                                default: DrawNoDrawingFunctionError(field); break;
                            }
                        }
                        else if (attr.Type == ConfigFieldTypes.SingleSelect)
                        {
                            if (field.FieldType.IsEnum)
                            {
                                var enumType = tweak.GetType().GetNestedType(attr.Options);
                                if (enumType == null)
                                {
                                    DrawNoDrawingFunctionError(field);
                                }
                                else
                                {
                                    var underlyingType = Enum.GetUnderlyingType(enumType);
                                    var data = Activator.CreateInstance(typeof(ConfigDrawData<>).MakeGenericType(new Type[] { underlyingType }))!;

                                    data.GetType().GetProperty("Tweak")!.SetValue(data, tweak);
                                    data.GetType().GetProperty("Config")!.SetValue(data, config);
                                    data.GetType().GetProperty("Field")!.SetValue(data, field);
                                    data.GetType().GetProperty("Attr")!.SetValue(data, attr);

                                    switch (underlyingType.Name)
                                    {
                                        case nameof(Int32): DrawSingleSelectEnumInt32((ConfigDrawData<int>)data, enumType); break;

                                        default: DrawNoDrawingFunctionError(field); break;
                                    }
                                }
                            }
                            else
                            {
                                var options = tweak.GetType().GetField(attr.Options)?.GetValue(tweak);
                                if (options is Dictionary<ClientLanguage, List<string>> opts)
                                {
                                    var data = new ConfigDrawData<string>()
                                    {
                                        Tweak = tweak,
                                        Config = config,
                                        Field = field,
                                        Attr = attr,
                                    };
                                    var list = opts[Service.ClientState.ClientLanguage];
                                    DrawSingleSelect(data, list);
                                }
                                else
                                {
                                    DrawNoDrawingFunctionError(field);
                                }
                            }
                        }
                        else
                        {
                            DrawNoDrawingFunctionError(field);
                        }

                        disabled?.Dispose();
                        indent?.Dispose();
                    }
                }
            }
        }
    }

    private static (string, Vector4) GetTweakStatus(Tweak tweak)
    {
        var status = "???";
        var color = ImGuiUtils.ColorGrey3;

        if (tweak.Outdated)
        {
            status = "Outdated";
            color = ImGuiUtils.ColorRed;
        }
        else if (!tweak.Ready)
        {
            status = "Initialization failed";
            color = ImGuiUtils.ColorRed;
        }
        else if (tweak.Enabled)
        {
            status = "Enabled";
            color = ImGuiUtils.ColorGreen;
        }
        else if (!tweak.Enabled)
        {
            status = "Disabled";
        }

        return (status, color);
    }

    private static void DrawNoDrawingFunctionError(FieldInfo field)
    {
        ImGuiHelpers.SafeTextColoredWrapped(ImGuiUtils.ColorRed, $"Could not find suitable drawing function for field \"{field.Name}\" (Type {field.FieldType.Name}).");
    }

    private static void DrawInvalidType(FieldInfo field)
    {
        ImGuiHelpers.SafeTextColoredWrapped(ImGuiUtils.ColorRed, $"Invalid type for \"{field.Name}\" (Type {field.FieldType.Name}).");
    }

    private static void DrawSingleSelectEnumInt32(ConfigDrawData<int> data, Type enumType)
    {
        var selectedLabel = "Invalid Option";

        var selectedName = Enum.GetName(enumType, data.Value);
        if (string.IsNullOrEmpty(selectedName))
        {
            ImGuiUtils.TextUnformattedColored(new Vector4(1, 0, 0, 1), $"Missing Name for Value {data.Value} in {enumType.Name}.");
        }
        else
        {
            var selectedAttr = (EnumOptionAttribute?)enumType.GetField(selectedName)?.GetCustomAttribute(typeof(EnumOptionAttribute));
            if (selectedAttr == null)
            {
                ImGuiUtils.TextUnformattedColored(new Vector4(1, 0, 0, 1), $"Missing EnumOptionAttribute for {selectedName} in {enumType.Name}.");
            }
            else
            {
                selectedLabel = selectedAttr.Label;
            }
        }

        ImGui.TextUnformatted(data.Label);

        using (ImGuiUtils.ConfigIndent())
        {
            using (var combo = ImRaii.Combo(data.Key, selectedLabel))
            {
                if (combo != null && combo.Success)
                {
                    var names = Enum.GetNames(enumType)
                        .Select(name => (
                            Name: name,
                            Attr: (EnumOptionAttribute?)enumType.GetField(name)?.GetCustomAttribute(typeof(EnumOptionAttribute))
                        ))
                        .Where(tuple => tuple.Attr != null)
                        .OrderBy((tuple) => tuple.Attr == null ? "" : tuple.Attr.Label);

                    foreach (var (Name, Attr) in names)
                    {
                        var value = (int)Enum.Parse(enumType, Name);

                        if (ImGui.Selectable(Attr!.Label, data.Value == value))
                            data.Value = value;

                        if (data.Value == value)
                            ImGui.SetItemDefaultFocus();
                    }
                }
            }

            DrawSettingsDescription(data);
        }
    }

    private static void DrawSingleSelect(ConfigDrawData<string> data, List<string> options)
    {
        ImGui.TextUnformatted(data.Label);

        using (ImGuiUtils.ConfigIndent())
        {
            using (var combo = ImRaii.Combo(data.Key, data.Value ?? ""))
            {
                if (combo != null && combo.Success)
                {
                    foreach (var item in options)
                    {
                        if (ImGui.Selectable(item, data.Value == item))
                            data.Value = item;

                        if (data.Value == item)
                            ImGui.SetItemDefaultFocus();
                    }
                }
            }

            DrawSettingsDescription(data);
        }
    }

    private static void DrawString(ConfigDrawData<string> data)
    {
        var value = data.Value;

        ImGui.TextUnformatted(data.Label);

        using (ImGuiUtils.ConfigIndent())
        {
            if (ImGui.InputText(data.Key, ref value, 50))
                data.Value = value;

            DrawResetButton(data);
            DrawSettingsDescription(data);
        }
    }

    private static void DrawFloat(ConfigDrawData<float> data)
    {
        var min = data.Attr != null ? data.Attr.Min : 0f;
        var max = data.Attr != null ? data.Attr.Max : 100f;

        var value = data.Value;

        ImGui.TextUnformatted(data.Label);

        using (ImGuiUtils.ConfigIndent())
        {
            if (ImGui.SliderFloat(data.Key, ref value, min, max))
                data.Value = value;

            DrawResetButton(data);
            DrawSettingsDescription(data);
        }
    }

    private static void DrawInt(ConfigDrawData<int> data)
    {
        var min = data.Attr != null ? data.Attr.Min : 0f;
        var max = data.Attr != null ? data.Attr.Max : 100f;

        var value = data.Value;

        ImGui.TextUnformatted(data.Label);

        using (ImGuiUtils.ConfigIndent())
        {
            if (ImGui.SliderInt(data.Key, ref value, (int)min, (int)max))
                data.Value = value;

            DrawResetButton(data);
            DrawSettingsDescription(data);
        }
    }

    private static void DrawBool(ConfigDrawData<bool> data)
    {
        var value = data.Value;

        if (ImGui.Checkbox(data.Label + data.Key, ref value))
            data.Value = value;

        DrawSettingsDescription(data, true);
    }

    private static void DrawResetButton<T>(ConfigDrawData<T> data)
    {
        if (data.Attr?.DefaultValue != null)
        {
            ImGui.SameLine();
            if (ImGuiUtils.IconButton($"##HaselTweaks_Config_{data.Tweak.InternalName}_{data.Field.Name}_Reset", FontAwesomeIcon.Undo, $"Reset to Default: {(T)data.Attr!.DefaultValue}"))
            {
                data.Value = (T)data.Attr!.DefaultValue;
            }
        }
    }

    private static void DrawColor4(ConfigDrawData<Vector4> data)
    {
        var value = data.Value;

        if (ImGui.ColorEdit4(data.Label + data.Key, ref value))
            data.Value = value;

        DrawSettingsDescription(data, true);
    }

    private static void DrawSettingsDescription(IConfigDrawData data, bool indent = false)
    {
        if (string.IsNullOrEmpty(data.Description))
            return;

        var _indent = indent ? ImGuiUtils.ConfigIndent() : null;

        ImGuiHelpers.SafeTextColoredWrapped(ImGuiUtils.ColorGrey, data.Description);

        _indent?.Dispose();

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3);
    }
}
