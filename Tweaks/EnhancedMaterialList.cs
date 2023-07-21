using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Memory;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HaselTweaks.Enums;
using HaselTweaks.Structs;
using HaselTweaks.Utils;
using Lumina.Excel.GeneratedSheets;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HaselTweaks.Tweaks;

[Tweak(
    Name: "Enhanced Material List",
    Description: "Enhances the Material List (and Recipe Tree)."
)]
public unsafe partial class EnhancedMaterialList : Tweak
{
    public static Configuration Config => Plugin.Config.Tweaks.EnhancedMaterialList;

    private DateTime _lastRecipeMaterialListRefresh = DateTime.Now;
    private bool _recipeMaterialListRefreshPending = false;
    private bool _recipeMaterialListLockPending = false;

    private DateTime _lastRecipeTreeRefresh = DateTime.Now;
    private bool _recipeTreeRefreshPending = false;

    private bool _handleRecipeResultItemContextMenu = false;

    public class Configuration
    {
        [ConfigField(
            Label = "Enable Zone Names",
            Description = "The zone with the lowest teleportation cost is displayed.\nA green zone name means it's the current zone.\nSince space is limited it has to shorten the item and zone name.",
            OnChange = nameof(RequestRecipeMaterialListRefresh)
        )]
        public bool EnableZoneNames = true;

        [ConfigField(
            Label = "Disable for Crystals",
            DependsOn = nameof(EnableZoneNames),
            OnChange = nameof(RequestRecipeMaterialListRefresh)
        )]
        public bool DisableZoneNameForCrystals = true;

        [ConfigField(
            Label = "Enable click to open Map"
        )]
        public bool ClickToOpenMap = true;

        [ConfigField(
            Label = "Disable for Crystals",
            DependsOn = nameof(ClickToOpenMap),
            OnChange = nameof(RequestRecipeMaterialListRefresh)
        )]
        public bool DisableClickToOpenMapForCrystals = true;

        [ConfigField(
            Label = "Auto-refresh Material List",
            Description = "Refreshes the material list after an item has been bought, crafted, fished or gathered."
        )]
        public bool AutoRefreshMaterialList = true;

        [ConfigField(
            Label = "Auto-refresh Recipe Tree",
            Description = "Refreshes the recipe tree after an item has been bought, crafted, fished or gathered."
        )]
        public bool AutoRefreshRecipeTree = true;

        [ConfigField(
            Label = "Restore Material List on Login",
            Description = "The material list will reopen with the same recipe and quantity each time you log in as long as the window is locked.",
            OnChange = nameof(SaveRestoreMaterialList)
        )]
        public bool RestoreMaterialList = true;

        [ConfigField(Type = ConfigFieldTypes.Ignore)]
        public uint RestoreMaterialListRecipeId = 0;

        [ConfigField(Type = ConfigFieldTypes.Ignore)]
        public uint RestoreMaterialListAmount = 0;

        [ConfigField(
            Label = "Add \"Search for Item by Crafting Method\" context menu entry",
            Description = "For the result item. No more need to open the recipe tree first."
        )]
        public bool AddSearchForItemByCraftingMethodContextMenuEntry = true; // yep, i spelled it out
    }

    public override void OnFrameworkUpdate(Dalamud.Game.Framework framework)
    {
        RefreshRecipeMaterialList();
        RefreshRecipeTree();
    }

    public override void OnAddonOpen(string addonName, AtkUnitBase* unitBase)
    {
        if (addonName is "Catch")
            RequestRefresh();
    }

    public override void OnAddonClose(string addonName, AtkUnitBase* unitBase)
    {
        if (addonName is "Synthesis" or "SynthesisSimple" or "Gathering" or "ItemSearchResult" or "InclusionShop" or "Shop" or "ShopExchangeCurrency" or "ShopExchangeItem")
            RequestRefresh();
    }

    private void RequestRefresh()
    {
        if (Config.AutoRefreshMaterialList)
            _recipeMaterialListRefreshPending = true;

        if (Config.AutoRefreshRecipeTree)
            _recipeTreeRefreshPending = true;
    }

    public override void OnTerritoryChanged(ushort e)
        => RequestRecipeMaterialListRefresh();

    public override void OnLogin()
    {
        if (Config.RestoreMaterialList &&
            Config.RestoreMaterialListRecipeId != 0 &&
            GetAgent<AgentRecipeMaterialList>(AgentId.RecipeMaterialList, out var agentRecipeMaterialList) &&
            agentRecipeMaterialList != null &&
            agentRecipeMaterialList->RecipeId != Config.RestoreMaterialListRecipeId)
        {
            Log("Restoring RecipeMaterialList");
            agentRecipeMaterialList->OpenByRecipeId(Config.RestoreMaterialListRecipeId, Math.Max(Config.RestoreMaterialListAmount, 1));

            _recipeMaterialListLockPending = true;
        }
    }

    private void RequestRecipeMaterialListRefresh()
        => _recipeMaterialListRefreshPending = true;

    private void RefreshRecipeMaterialList()
    {
        if (!_recipeMaterialListRefreshPending && !_recipeMaterialListLockPending)
            return;

        if (DateTime.Now.Subtract(_lastRecipeMaterialListRefresh).TotalSeconds < 2)
            return;

        if (!GetAddon<AddonRecipeMaterialList>(AgentId.RecipeMaterialList, out var recipeMaterialList))
        {
            _recipeMaterialListRefreshPending = false;
            return;
        }

        if (_recipeMaterialListRefreshPending)
        {
            Log("Refreshing RecipeMaterialList");
            using var atkEvent = new DisposableStruct<AtkEvent>();
            recipeMaterialList->ReceiveEvent(AtkEventType.ButtonClick, 1, atkEvent, 0);

            _recipeMaterialListRefreshPending = false;
        }

        if (_recipeMaterialListLockPending)
        {
            recipeMaterialList->SetWindowLock(true);
            _recipeMaterialListLockPending = false;
        }

        _lastRecipeMaterialListRefresh = DateTime.Now;
    }

    private void RefreshRecipeTree()
    {
        if (!_recipeTreeRefreshPending)
            return;

        if (DateTime.Now.Subtract(_lastRecipeTreeRefresh).TotalSeconds < 2)
            return;

        if (!GetAddon<AddonRecipeTree>(AgentId.RecipeTree, out var recipeTree))
        {
            _recipeTreeRefreshPending = false;
            return;
        }

        Log("Refreshing RecipeTree");
        using var atkEvent = new DisposableStruct<AtkEvent>();
        recipeTree->ReceiveEvent(AtkEventType.ButtonClick, 0, atkEvent, 0);

        _lastRecipeTreeRefresh = DateTime.Now;
        _recipeTreeRefreshPending = false;
    }

    private void SaveRestoreMaterialList()
    {
        if (!GetAgent<AgentRecipeMaterialList>(AgentId.RecipeMaterialList, out var agentRecipeMaterialList))
            return;

        var shouldSave = Config.RestoreMaterialList && agentRecipeMaterialList != null && agentRecipeMaterialList->WindowLocked;
        Config.RestoreMaterialListRecipeId = shouldSave ? agentRecipeMaterialList->RecipeId : 0u;
        Config.RestoreMaterialListAmount = shouldSave ? agentRecipeMaterialList->Amount : 0u;
        Plugin.Config.Save();
    }

    [VTableHook<AgentRecipeMaterialList>((int)AgentInterfaceVfs.ReceiveEvent)]
    public nint AgentRecipeMaterialList_ReceiveEvent(AgentRecipeMaterialList* agent, nint a2, nint a3, nint a4, nint a5)
    {
        var ret = AgentRecipeMaterialList_ReceiveEventHook.OriginalDisposeSafe(agent, a2, a3, a4, a5);
        SaveRestoreMaterialList();
        return ret;
    }

    [VTableHook<AddonRecipeMaterialList>((int)AtkResNodeVfs.ReceiveEvent)]
    public void AddonRecipeMaterialList_ReceiveEvent(AddonRecipeMaterialList* addon, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, nint a5)
    {
        AddonRecipeMaterialList_ReceiveEventHook.OriginalDisposeSafe(addon, eventType, eventParam, atkEvent, a5);

        if (!Config.ClickToOpenMap)
            return;

        if (eventType != AtkEventType.ListItemToggle)
            return;

        if (*(byte*)(a5 + 0x18) == 1) // ignore right click
            return;

        var rowData = **(nint**)(a5 + 0x08);
        var itemId = *(uint*)(rowData + 0x04);

        var item = Service.Data.GetExcelSheet<Item>()?.GetRow(itemId);
        if (item == null)
            return;

        if (Config.DisableClickToOpenMapForCrystals && item.ItemUICategory.Row == 59)
            return;

        var tuple = GetPointForItem(itemId);
        if (tuple == null)
            return;

        var (totalPoints, point, cost, isSameZone, placeName) = tuple.Value;

        OpenMapWithGatheringPoint(point, item);
    }

    [AddressHook<AddonRecipeMaterialList>(nameof(AddonRecipeMaterialList.Addresses.SetupRow))]
    public void AddonRecipeMaterialList_SetupRow(AddonRecipeMaterialList* addon, nint a2, nint a3)
    {
        AddonRecipeMaterialList_SetupRowHook.OriginalDisposeSafe(addon, a2, a3);

        if (!Config.EnableZoneNames)
            return;

        var data = **(nint**)(a2 + 0x08);
        var itemId = *(uint*)(data + 0x04);

        // TODO: only for missing items?

        var item = Service.Data.GetExcelSheet<Item>()?.GetRow(itemId);
        if (item == null)
            return;

        // Exclude Crystals
        if (Config.DisableZoneNameForCrystals && item.ItemUICategory.Row == 59)
            return;

        var tuple = GetPointForItem(itemId);
        if (tuple == null)
            return;

        var (totalPoints, point, cost, isSameZone, placeNameSeString) = tuple.Value;

        var nameNode = *(AtkTextNode**)(a3 + 0x08);
        if (nameNode == null)
            return;

        var textPtr = (nint)nameNode->GetText();
        if (textPtr == 0)
            return;

        // when you don't know how to add text nodes... Sadge

        nameNode->AtkResNode.Y = 14;
        nameNode->AtkResNode.Flags_2 |= 0x1;

        nameNode->TextFlags = 192; // allow multiline text (not sure on the actual flags it sets though)
        nameNode->LineSpacing = 17;

        var itemName = MemoryHelper.ReadSeStringNullTerminated(textPtr).TextValue.Replace("\r\n", "");
        if (itemName.Length > 23)
            itemName = itemName[..20] + "...";

        var placeName = placeNameSeString.TextValue;
        if (placeName.Length > 23)
            placeName = placeName[..20] + "...";

        var sb = new SeStringBuilder()
            .AddText(itemName)
            .Add(NewLinePayload.Payload)
            .AddUiForeground((ushort)(isSameZone ? 570 : 4))
            .AddUiGlow(550)
            .AddText(placeName)
            .AddUiGlowOff()
            .AddUiForegroundOff();

        nameNode->SetText(sb.Encode());
    }

    [AddressHook<AgentRecipeMaterialList>(nameof(AgentRecipeMaterialList.Addresses.OpenRecipeResultItemContextMenu))]
    public nint AgentRecipeMaterialList_OpenRecipeResultItemContextMenu(AgentRecipeMaterialList* agent)
    {
        _handleRecipeResultItemContextMenu = true;
        return AgentRecipeMaterialList_OpenRecipeResultItemContextMenuHook.OriginalDisposeSafe(agent);
    }

    [AddressHook<AgentRecipeItemContext>(nameof(AgentRecipeItemContext.Addresses.AddItemContextMenuEntries))]
    public nint AgentRecipeItemContext_AddItemContextMenuEntries(AgentRecipeItemContext* agent, uint itemId, byte flags, byte* itemName)
    {
        if (!_handleRecipeResultItemContextMenu)
            goto originalAddItemContextMenuEntries;

        _handleRecipeResultItemContextMenu = false;

        if (!Config.AddSearchForItemByCraftingMethodContextMenuEntry)
            goto originalAddItemContextMenuEntries;

        if (!GetAgent<AgentRecipeMaterialList>(AgentId.RecipeMaterialList, out var agentRecipeMaterialList))
            goto originalAddItemContextMenuEntries;

        if (GetAddon(AgentId.RecipeMaterialList) == null)
            goto originalAddItemContextMenuEntries;

        if (agentRecipeMaterialList->Recipe == null || agentRecipeMaterialList->Recipe->ResultItemId != itemId)
            goto originalAddItemContextMenuEntries;

        var localPlayer = (Character*)(Service.ClientState.LocalPlayer?.Address ?? 0);
        if (localPlayer == null || localPlayer->EventState == 5)
            goto originalAddItemContextMenuEntries;

        flags |= 2;

originalAddItemContextMenuEntries:
        return AgentRecipeItemContext_AddItemContextMenuEntriesHook.OriginalDisposeSafe(agent, itemId, flags, itemName);
    }

    private (int, GatheringPoint, uint, bool, SeString)? GetPointForItem(uint itemId)
    {
        var GatheringItemSheet = Service.Data.GetExcelSheet<GatheringItem>();
        var GatheringPointBaseSheet = Service.Data.GetExcelSheet<GatheringPointBase>();
        var GatheringPointSheet = Service.Data.GetExcelSheet<GatheringPoint>();

        if (GatheringItemSheet == null || GatheringPointBaseSheet == null || GatheringPointSheet == null)
            return null;

        var gatheringItem = GatheringItemSheet.FirstOrDefault(row => row?.Item == itemId, null);
        if (gatheringItem == null)
            return null;

        var gatheringPoints = GatheringPointBaseSheet
            .Where(row => row.Item.Any(item => item == gatheringItem.RowId))
            .Select(row => GatheringPointSheet.FirstOrDefault(gprow => gprow?.GatheringPointBase.Row == row.RowId && gprow.TerritoryType.Row > 1, null))
            .Where(row => row != null)
            /* not needed?
            .GroupBy(row => row!.RowId)
            .Select(rows => rows.First())
            */
            .ToList();

        if (gatheringPoints == null || !gatheringPoints.Any())
            return null;

        var currentTerritoryTypeId = GameMain.Instance()->CurrentTerritoryTypeId;
        var point = gatheringPoints.FirstOrDefault(row => row?.TerritoryType.Row == currentTerritoryTypeId, null);
        var isSameZone = point != null;
        var cost = 0u;
        if (point == null)
        {
            foreach (var p in gatheringPoints)
            {
                var thisCost = (uint)Telepo.GetTeleportCost((ushort)currentTerritoryTypeId, (ushort)p!.TerritoryType.Row, false, false, false);
                if (cost == 0 || thisCost < cost)
                {
                    cost = thisCost;
                    point = p;
                }
            }
        }

        if (point == null)
            return null;

        var placeName = point.TerritoryType.Value?.PlaceName.Value?.Name.ToDalamudString();
        return placeName == null ? null : (gatheringPoints.Count, point, cost, isSameZone, placeName);
    }

    public bool OpenMapWithGatheringPoint(GatheringPoint? gatheringPoint, Item? item = null)
    {
        if (!GetAgent<AgentMap>(AgentId.Map, out var agentMap))
            return false;

        if (gatheringPoint == null)
            return false;

        var territoryType = gatheringPoint.TerritoryType.Value;
        if (territoryType == null)
            return false;

        var gatheringPointBase = gatheringPoint.GatheringPointBase.Value;
        if (gatheringPointBase == null)
            return false;

        var exportedPoint = Service.Data.GetExcelSheet<ExportedGatheringPoint>()?.GetRow(gatheringPointBase.RowId);
        if (exportedPoint == null)
            return false;

        var gatheringType = exportedPoint.GatheringType.Value;
        if (gatheringType == null)
            return false;

        var raptureTextModule = RaptureTextModule.Instance();

        var levelText = gatheringPointBase.GatheringLevel == 1
            ? raptureTextModule->GetAddonText(242) // "Lv. ???"
            : raptureTextModule->FormatAddonText2(35, gatheringPointBase.GatheringLevel, 0);
        var gatheringPointName = Statics.GetGatheringPointName(
            &raptureTextModule,
            (byte)exportedPoint.GatheringType.Row,
            exportedPoint.GatheringPointType
        );

        using var tooltip = new DisposableUtf8String(levelText);
        tooltip.AppendString(" ");
        tooltip.AppendString(gatheringPointName);

        var iconId = Statics.IsGatheringPointRare(exportedPoint.GatheringPointType) == 0
            ? gatheringType.IconMain
            : gatheringType.IconOff;

        agentMap->TempMapMarkerCount = 0;
        agentMap->AddGatheringTempMarker(
            4u,
            (int)Math.Round(exportedPoint.X),
            (int)Math.Round(exportedPoint.Y),
            (uint)iconId,
            exportedPoint.Radius,
            tooltip
        );

        var titleBuilder = new SeStringBuilder().AddText("\uE078");
        if (item != null)
        {
            titleBuilder
                .AddText(" ")
                .AddUiForeground(549)
                .AddUiGlow(550)
                .Append(item.Name.ToDalamudString())
                .AddUiGlowOff()
                .AddUiForegroundOff();
        }

        using var title = new DisposableUtf8String(titleBuilder.BuiltString);

        var mapInfo = stackalloc OpenMapInfo[1];
        mapInfo->Type = FFXIVClientStructs.FFXIV.Client.UI.Agent.MapType.GatheringLog;
        mapInfo->MapId = territoryType.Map.Row;
        mapInfo->TerritoryId = territoryType.RowId;
        mapInfo->TitleString = *title.Ptr;
        agentMap->OpenMap(mapInfo);

        return true;
    }
}
