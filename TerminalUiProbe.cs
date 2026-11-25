using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using Terraria.GameContent;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace AEBeyondDimensions;

internal static class TerminalSounds
{
    internal static readonly SoundStyle ButtonClick = Terraria.ID.SoundID.MenuTick with
    {
        Volume = .25f,
        MaxInstances = 4
    };

    internal static void PlayClick() => SoundEngine.PlaySound(ButtonClick);
}

public sealed class ProbeMod : Mod
{
    public ProbeMod() => PreviousNameMigration.CopySettings();
    public override void HandlePacket(System.IO.BinaryReader reader,int whoAmI)=>TeamNetwork.Handle(reader,whoAmI);
    public override object Call(params object[] args)
    {
        if (args is ["GetTerminalDimensionContext"])
            return ModContent.GetInstance<TerminalDimensionContext>().Read();
        if (args is ["GetSearchSettings", Player p])
        {
            var prefs = p.GetModPlayer<TerminalSearchSettings>();
            return (Provider:prefs.JeiActive?"jei":"ae", SyncWithJei:prefs.Sync && !prefs.JeiActive && prefs.JeiAvailable, prefs.Remember, prefs.AutoFocus);
        }
        if (args is ["SetSearchSettings", Player player, bool remember, bool autoFocus])
        {
            player.GetModPlayer<TerminalSearchSettings>().Set(remember,autoFocus);
            return true;
        }
        return TerminalOrders.Dispatch(args);
    }
    public override void Unload() => TerminalOrders.UnloadProviders();
}

public sealed class TerminalInputPlayer : ModPlayer
{
    public override void SetControls()
    {
        if (!ModContent.GetInstance<TerminalSystem>().CapturesMovement) return;
        Player.controlLeft = Player.controlRight = Player.controlUp = Player.controlDown = false;
        if(TeamClient.Open){Player.controlUseItem=false;Player.controlUseTile=false;}
        Player.controlJump = Player.controlHook = Player.controlMount = false;
    }

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        var terminal = ModContent.GetInstance<TerminalSystem>();
        terminal.HandleOpenKey(Player);
    }
}

public sealed class TerminalSystem : ModSystem
{
    internal ModKeybind? OpenKey { get; private set; }
    private UserInterface? terminalInterface;
    private TerminalState? terminal;
    private GameTime latestTime = new();
    private bool inventoryWasOpen;
    internal UIElement? ControlRoot;
    internal bool ReturningFromControl;

    public override void Load()
    {
        if (Main.dedServ)
            return;

        OpenKey = KeybindLoader.RegisterKeybind(Mod, "ToggleTerminal", Keys.O);
        terminalInterface = new UserInterface();
        terminal = new TerminalState();
        On_GameInterfaceLayer.Draw += DrawLayer;
        On_Player.QuickMount += HandleQuickMount;
    }

    private void HandleQuickMount(On_Player.orig_QuickMount original, Player player)
    {
        if (player.whoAmI == Main.myPlayer && CapturesMovement) return;
        original(player);
    }

    private GameInterfaceLayer? terminalLayer;
    private string? sidebarLayerName;

    // Decide at draw time, including layers inserted after our ModifyInterfaceLayers.
    // Preserve other mods' insertion anchors and their original Active flags.
    private bool DrawLayer(On_GameInterfaceLayer.orig_Draw original, GameInterfaceLayer layer)
    {
        if (!CapturesMovement || Main.gameMenu || Main.ingameOptionsWindow)
            return original(layer);

        bool essential = layer.Name is "Vanilla: Cursor" or "Vanilla: Mouse Item / NPC Head" ||
            layer.Name.StartsWith("Vanilla: Interface Logic ", StringComparison.Ordinal);
        bool panel = TerminalGuide.Instance.Open
            ? ReferenceEquals(layer, TerminalGuide.Instance.GuideLayer)
            : ReferenceEquals(layer, terminalLayer) || layer.Name == sidebarLayerName;
        if (essential || panel) return original(layer);
        return true;
    }

    private Func<bool>? browserOpen, browserOwnsInput;
    internal Action<(int Type,int Need)[]>? BookmarkMissing;
    internal Func<string>? ReadJei;
    internal Action<string>? WriteJei;
    internal Func<int,bool>? JeiMatches;
    internal bool BrowserOwnsInput => browserOwnsInput?.Invoke() == true;

    public override void PostSetupContent()
    {
        if (!Main.dedServ)
        {
            // Queue the small terminal skins through the asset repository during mod loading.
            foreach (string file in Mod.GetFileNames())
            {
                if (file.StartsWith("Assets/",StringComparison.Ordinal) && file.EndsWith(".rawimg",StringComparison.Ordinal))
                    Mod.Assets.Request<Texture2D>(file[..^7], ReLogic.Content.AssetRequestMode.AsyncLoad);
            }
        }

        if (!Main.dedServ && ModLoader.TryGetMod("JustEnoughItemsTerraria", out var browser)
            && browser.Version >= new Version(0, 1, 30))
        {
            (browserOpen, browserOwnsInput) = ((Func<bool>, Func<bool>))browser.Call("RegisterTerminalSidebars",
                (Func<int>)(() => terminalInterface?.CurrentState != null && !TerminalGuide.Instance.Open &&
                    terminal?.SettingsOpen == false && terminal.OrdersOpen == false
                    ? (int)Math.Ceiling(ResultRow.FullWidth * Main.UIScale) : 0),
                (Func<int>)(() => TerminalTooltip.HoveredType),
                (Func<bool>)(() => Main.netMode != Terraria.ID.NetmodeID.Server && OpenKey?.JustPressed == true));
            if (browser.Version >= new Version(0,1,71))
                TerminalTooltip.ItemDetails = (Func<Item,string>)browser.Call("ItemHoverDetails");
            if (browser.Version >= new Version(0,1,70))
                browser.Call("RegisterTerminalMapExit", (Action)(() => { if (terminalInterface?.CurrentState != null) Toggle(); }));
            sidebarLayerName = "Adaptive Recipe Browser: Query";
            if (browser.Version >= new Version(0,1,39))
                browser.Call("RegisterTerminalGuide", (Func<bool>)(()=>TerminalGuide.Instance.Open));
            if (browser.Version >= new Version(0,1,55))
                browser.Call("RegisterBenchMaterials", (Func<int,(int,bool)[]>)(id =>
                    Main.LocalPlayer.GetModPlayer<TerminalBench>().MaterialStatus(id)));
            if(browser.Version>=new Version(0,1,40))
                browser.Call("RegisterRecipeBench",(Func<int,bool,bool>)((id,commit)=>{
                    var player=Main.LocalPlayer;
                    if(Main.netMode==Terraria.ID.NetmodeID.MultiplayerClient && !TeamClient.Open){
                        if(!TeamSupply.Active || TeamClient.InventoryTeam!=player.team || !player.GetModPlayer<TerminalBench>().Prepare(id,false))return false;
                        return !commit || TeamClient.SelectBench(id);
                    }
                    if(terminal==null || !player.GetModPlayer<TerminalStorage>().CanAccess(commit) || !player.GetModPlayer<TerminalBench>().Prepare(id,commit))return false;
                    if(commit){ if(terminalInterface?.CurrentState==null)Toggle();terminal.SelectBench(); }
                    return true;
                }));
            if (browser.Version >= new Version(0,1,34))
                (ReadJei,WriteJei,JeiMatches) = ((Func<string>,Action<string>,Func<int,bool>))browser.Call("TerminalSearch");
            if(browser.Version>=new Version(0,1,75))
                browser.Call("RegisterOrderBookmarkCancel",(Action<string>)(id=>Main.LocalPlayer.GetModPlayer<TerminalOrders>().CancelBookmarkOrder(id)));
            if(browser.Version>=new Version(0,1,66))
                TerminalOrders.NewGroupsFirst=(Func<bool>)browser.Call("RegisterOrderBookmarkGroups",
                    (Func<(string,int,int,int,int)[]>)(()=>Main.LocalPlayer.GetModPlayer<TerminalOrders>().GroupedBookmarks()),
                    (Action<string[]>)(ids=>Main.LocalPlayer.GetModPlayer<TerminalOrders>().ReorderGroups(ids)));
            if (browser.Version >= new Version(0,1,62))
                browser.Call("RegisterOrderBookmarks",(Func<(int,int)[]>)(()=>Main.LocalPlayer.GetModPlayer<TerminalOrders>().BookmarkGoals),
                    (Func<Dictionary<int,int>>)InstantCraft.AvailableCounts);
            else if (browser.Version >= new Version(0,1,32))
                BookmarkMissing = (Action<(int,int)[]>)browser.Call("RegisterMissingBookmarks",
                    (Func<System.Collections.Generic.Dictionary<int,int>>)InstantCraft.AvailableCounts);
            if (browser.Version >= new Version(0,1,31))
                browser.Call("RegisterTerminalOrders", (Func<int,bool>)(type => Main.netMode==Terraria.ID.NetmodeID.MultiplayerClient ? TeamClient.Select(type) : InstantCraft.Open(Main.LocalPlayer,type)));
        }
    }

    internal bool CapturesMovement => TeamClient.Open || terminalInterface?.CurrentState != null || TerminalGuide.Instance.Open;
    internal bool TypingSearch => terminalInterface?.CurrentState != null && terminal?.EditingSearch == true;

    private bool handledEscape;
    private bool EscapeBound => OpenKey?.GetAssignedKeys().Contains(nameof(Keys.Escape))==true;
    internal void HandleOpenKey(Player player)
    {
        bool escape=Main.keyState.IsKeyDown(Keys.Escape) && EscapeBound;
        if(!escape) {
            if(!TerminalGuide.Instance.Open && OpenKey?.JustPressed==true && !TypingSearch && !BrowserOwnsInput)
                Toggle();
            return;
        }
        if(Main.gameMenu || Main.netMode==Terraria.ID.NetmodeID.Server || player.dead ||
            Main.drawingPlayerChat || Main.editChest || Main.editSign || Main.blockInput ||
            Main.ingameOptionsWindow || Main.mapFullscreen || player.chest!=-1 || player.talkNPC!=-1 ||
            ReturningFromControl || TerminalGuide.Instance.Open || TerminalGuide.Instance.ClosedThisTick || BrowserOwnsInput)
            return;
        if(Main.netMode==Terraria.ID.NetmodeID.SinglePlayer && !player.GetModPlayer<TerminalStorage>().CanAccess(false))return;
        // Inventory normally shares Escape. Suppress its player control on each held frame.
        player.controlInv=false;
        if(handledEscape || Main.oldKeyState.IsKeyDown(Keys.Escape))return;
        handledEscape=true;
        if(terminalInterface?.CurrentState==null)Toggle();
        else EscapeTerminal();
    }

    private void EscapeTerminal()
    {
        if(terminal!.CloseControlPanel())Main.playerInventory=true;
        else if(terminal.ChoicesOpen){terminal.CloseChoices();Main.playerInventory=true;}
        else Toggle();
    }

    internal void ShowTeamBench()=>terminal?.SelectBench();
    internal void BeginTeamVisit(){
        inventoryWasOpen=Main.playerInventory;Main.playerInventory=true;
        terminalInterface?.SetState(terminal);
    }
    internal void EndTeamVisit(){
        terminal?.CloseChoices();terminalInterface?.SetState(null);
        Main.playerInventory=inventoryWasOpen || !Main.mouseItem.IsAir;
    }
    internal void Toggle()
    {
        if(Main.netMode==Terraria.ID.NetmodeID.MultiplayerClient){
            if(TeamClient.Open)TeamClient.Close();else TeamClient.Request();
            return;
        }
        if (ReturningFromControl || terminalInterface == null || Main.LocalPlayer.dead || Main.netMode != Terraria.ID.NetmodeID.SinglePlayer)
            return;
        if (terminalInterface.CurrentState == null)
        {
            var storage=Main.LocalPlayer.GetModPlayer<TerminalStorage>();
            if (!storage.CanAccess(true)) return;
            SingleTerminal.MergeLegacy();
            storage.BeginTerminalVisit();
            inventoryWasOpen = Main.playerInventory;
            Main.playerInventory = true;
            terminalInterface.SetState(terminal);
        }
        else
        {
            CloseTerminal();
            Main.playerInventory = inventoryWasOpen || !Main.mouseItem.IsAir;
        }
    }

    internal void LeaveForControlFunction()
    {
        ReturningFromControl=true;
        CloseTerminal();
        Main.playerInventory=inventoryWasOpen || !Main.mouseItem.IsAir;
    }

    private void CloseTerminal()
    {
        if(TeamClient.Open){TeamClient.Close();return;}
        terminal?.CloseChoices();
        Main.LocalPlayer.GetModPlayer<TerminalReforgePlayer>().ReturnInput();
        terminalInterface?.SetState(null);
    }

    internal void OpenController()
    {
        if (terminalInterface?.CurrentState == null) Toggle();
        else SingleTerminal.MergeLegacy();
    }

    internal void OpenOrders()
    {
        if (terminalInterface == null || Main.LocalPlayer.dead) return;
        if (terminalInterface.CurrentState == null) Toggle();
        terminal?.OpenOrders();
    }

    public override void UpdateUI(GameTime gameTime)
    {
        latestTime = gameTime;
        if(!Main.gameMenu && terminal!=null && !terminal.LayoutReady)
            terminal.PrepareLayout();

        if(!Main.hasFocus || Main.gameMenu || TerminalGuide.Instance.Open || BrowserOwnsInput ||
            terminal?.ModalSettingsOpen==true)terminal?.SetSearchFocus(false);
        if(Main.keyState.IsKeyUp(Keys.Escape))handledEscape=false;
        if (ReturningFromControl)
        {
            if (Main.LocalPlayer.dead || Main.gameMenu) { ClearControlVisit(); return; }
            if (ControlRoot != null && ControlVisit.IsOpen(ControlRoot)) return;
            if (Main.keyState.IsKeyDown(Keys.Escape)) return;
            ClearControlVisit();
            Main.playerInventory=inventoryWasOpen;
            Toggle();
            return;
        }
        if (browserOpen?.Invoke() == true && !Main.LocalPlayer.dead)
        {
            if (terminalInterface?.CurrentState != null) Main.playerInventory = true;
            terminal?.SetSearchFocus(false);
            return;
        }
        if (TerminalGuide.Instance.Open) return;
        if (terminalInterface?.CurrentState == null)
            return;
        if ((BrowserOwnsInput || terminal!.ChoicesOpen) && !Main.playerInventory) Main.playerInventory = true;
        if (!BrowserOwnsInput && TerminalGuide.Instance.ClosedThisTick == false && Main.keyState.IsKeyDown(Keys.Escape) && Main.oldKeyState.IsKeyUp(Keys.Escape) && !Main.LocalPlayer.dead)
        {
            if(!handledEscape){
                handledEscape=EscapeBound;
                EscapeTerminal();
            }
            if(terminalInterface.CurrentState!=null)Main.playerInventory=true;
            return;
        }
        if (!Main.playerInventory || Main.LocalPlayer.dead)
            CloseTerminal();
        else if (terminal?.ModalSettingsOpen == true)
        {
            Main.LocalPlayer.mouseInterface = true;
            PlayerInput.LockVanillaMouseScroll("TerminalSettings");
        }
        else if (terminal?.OrdersOpen == true)
            terminal.UpdateOrderDialog();
        else
            terminalInterface.Update(gameTime);
    }

    public override void Unload()
    {
        TerminalTooltip.ItemDetails = null;
        terminal?.SetSearchFocus(false);
    }

    private void ClearControlVisit()
    {
        ReturningFromControl=false;
        ControlRoot=null;
    }

    public override void OnWorldUnload()
    {
        ClearControlVisit();
        terminalInterface?.SetState(null);
        TerminalCursor.Clear();
        if (!Main.dedServ)
        {
            if (ReLogic.Content.AssetRepository.IsMainThread) ReceiptIcon.Clear();
            else Main.RunOnMainThread(ReceiptIcon.Clear).GetAwaiter().GetResult();
        }
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        if (browserOpen?.Invoke() == true || TerminalGuide.Instance.Open || (!TeamClient.Open && terminalInterface?.CurrentState == null))
            return;

        var beforeTooltip = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (beforeTooltip < 0) beforeTooltip = layers.FindIndex(layer => layer.Name == "Vanilla: Cursor");
        terminalLayer = new LegacyGameInterfaceLayer("AEBeyondDimensions: Browser", () =>
            {
                TerminalSkin.BeginPixelBatch(Main.spriteBatch);
                TerminalTooltip.BeginFrame();
                var viewport = terminalInterface.GetDimensions().ToRectangle();
                if (terminal?.ModalSettingsOpen == true || terminal?.OrdersOpen == true)
                {
                    if (terminal.ChoicesOpen)
                    {
                        int mx=Main.mouseX, my=Main.mouseY;
                        try { Main.mouseX=Main.mouseY=-10000; terminalInterface.Draw(Main.spriteBatch,latestTime); }
                        finally { Main.mouseX=mx; Main.mouseY=my; }
                    }
                    if (terminal.ModalSettingsOpen) terminal.DrawSettings(Main.spriteBatch,viewport);
                    else terminal.DrawOrderDialog(Main.spriteBatch, viewport);
                }
                else
                    terminalInterface.Draw(Main.spriteBatch, latestTime);
                TerminalTooltip.Draw(Main.spriteBatch, viewport);
                return true;
            }, InterfaceScaleType.UI);
        layers.Insert(beforeTooltip < 0 ? layers.Count : beforeTooltip, terminalLayer);
    }
}

internal sealed class TerminalState : UIState
{
    private const int HeaderHeight = 30;
    private const int SectionGap = 4;
    private readonly UIElement panel = new();
    private readonly UIElement mainFrame = new();
    private readonly UIList matches = new();
    private readonly UIScrollbar scrollbar = new TerminalBagScroll();
    private readonly UIElement queryLabel = new();
    private readonly EquipmentPane equipment = new() { OverrideSamplerState = SamplerState.PointClamp };
    internal void SelectBench() => equipment.SelectBench();
    private StoragePane storage = null!;
    private string query = "";
    private bool editing;
    private ulong caretStart;

    public bool SettingsOpen { get; private set; }
    internal bool ChoicesOpen => equipment.Choices.Open;
    internal void CloseChoices() => equipment.Choices.Close();
    public bool ModalSettingsOpen => SettingsOpen || equipment.Choices.Open;
    private bool magnetSettings;
    private bool controlSettings;
    private bool statusOpen;
    private readonly OrderStatusPanel statusPanel=new();
    private readonly ControlCardPanel controlPanel=new();
    internal bool CloseControlPanel()
    {
        if (!(controlSettings || statusOpen) || !SettingsOpen) return false;
        statusOpen=controlSettings=SettingsOpen=false;
        return true;
    }
    public bool EditingSearch => editing;
    internal void SetSearchFocus(bool focused)
    {
        if(editing==focused)return;
        editing=focused;
        if (focused) selection.Focus(query);
    }
    public override void OnDeactivate() => SetSearchFocus(false);
    private static TerminalSearchSettings Preferences => Main.LocalPlayer.GetModPlayer<TerminalSearchSettings>();
    public void DrawSettings(SpriteBatch batch, Rectangle viewport)
    {
        if (statusOpen) {
            SettingsOpen=statusOpen=statusPanel.Draw(batch,viewport);
            return;
        }
        if (controlSettings) {
            SettingsOpen=Main.LocalPlayer.GetModPlayer<TerminalMagnet>().ControlAvailable && controlPanel.Draw(batch,viewport);
            controlSettings=SettingsOpen;
            return;
        }
        if (equipment.Choices.Open) { equipment.Choices.Draw(batch,viewport); return; }
        var magnet = Main.LocalPlayer.GetModPlayer<TerminalMagnet>();
        SettingsOpen = (!magnetSettings || magnet.Available) &&
            Preferences.Draw(batch,viewport,magnetSettings ? magnet : null);
    }

    private void OpenSettings(bool magnet)
    {
        statusOpen=false;
        controlSettings=false;
        SetSearchFocus(false);
        magnetSettings = magnet;
        SettingsOpen = true;
    }
    public bool OrdersOpen => equipment.OrdersOpen;
    public void UpdateOrderDialog()
    {
        SetSearchFocus(false);
        equipment.UpdateOrderDialog();
    }
    public void DrawOrderDialog(SpriteBatch batch, Rectangle viewport) => equipment.DrawOrderDialog(batch, viewport);

    public void OpenOrders()
    {
        SetSearchFocus(false);
        SettingsOpen=false;
        equipment.OpenOrders();
    }

    internal bool LayoutReady { get; private set; }
    public override void OnInitialize()=>PrepareLayout();

    internal void PrepareLayout()
    {
        if(LayoutReady)return;
        panel.Width.Set(ResultRow.FullWidth, 0);
        panel.PaddingTop = panel.PaddingBottom = 12;
        mainFrame.Width.Set(0, ResultRow.FrameWidth / (float)ResultRow.FullWidth);
        mainFrame.MaxWidth.Set(ResultRow.FrameWidth, 0);
        mainFrame.HAlign = .5f;
        mainFrame.VAlign = .5f;
        Append(mainFrame);
        panel.HAlign = 0.5f;
        panel.VAlign = .5f;
        Append(panel);

        queryLabel.Left.Set(0, .49f);
        queryLabel.Width.Set(0,.32f);
        queryLabel.Height.Set(24,0);

        panel.Append(queryLabel);

        storage = new StoragePane(equipment,() => OpenSettings(false),() => OpenSettings(true),
            () => { OpenSettings(false); controlSettings=true; controlPanel.Reset(); }, () => mainFrame.GetDimensions().ToRectangle())
        {
            OverrideSamplerState = SamplerState.PointClamp
        };
        storage.Top.Set(HeaderHeight, 0);
        storage.Left.Set(0, ResultRow.BackpackLeftFraction);
        storage.Width.Set(0, 1 - 2 * ResultRow.BackpackLeftFraction);
        panel.Append(storage);

        matches.ListPadding = 0;
        matches.Width.Set(0, 1);
        matches.ManualSortMethod = _ => { };
        panel.Append(matches);

        scrollbar.Left.Set(0, ResultRow.BackpackRightFraction + 4f / ResultRow.FullWidth);
        panel.Append(scrollbar);
        matches.SetScrollbar(scrollbar);
        equipment.Left.Set(0, ResultRow.BackpackLeftFraction);
        equipment.Width.Set(0, 1 - 2 * ResultRow.BackpackLeftFraction);
        panel.Append(equipment);
        for(int row=0;row<5;row++){
            int first=(row+1)%5*ResultRow.ColumnCount;
            matches.Add(new ResultRow(Enumerable.Range(first,ResultRow.ColumnCount).ToArray(),matches,equipment,sweep));
        }
        LayoutReady=true;
    }

    internal static (int Rows,int BagHeight) MeasureStorageLayout(int availableHeight,int fixedHeight,
        int verticalPadding,TerminalStorage.TerminalLength length)
    {
        int bagHeight = Math.Clamp(availableHeight-fixedHeight-StoragePane.Step, 0, ResultRow.BagHeight);
        int capacity = Math.Clamp((availableHeight-fixedHeight-bagHeight)/StoragePane.Step, 1, 6);
        int requested = length switch {
            TerminalStorage.TerminalLength.Short => 2, TerminalStorage.TerminalLength.Long => 4, _ => 6
        };
        return (Math.Min(capacity,requested),bagHeight);
    }

    private void UpdateLayout()
    {
        int availableHeight=(int)GetDimensions().Height-12;
        if(availableHeight<=0) return;
        int padding=(int)(panel.PaddingTop+panel.PaddingBottom);
        int equipmentHeight=(int)equipment.Height.Pixels;
        int fixedHeight=padding+HeaderHeight+equipmentHeight+2*SectionGap+30;
        var (rows,bagHeight)=MeasureStorageLayout(availableHeight,fixedHeight,padding,
            Main.LocalPlayer.GetModPlayer<TerminalStorage>().Length);
        int storageHeight=rows*StoragePane.Step;
        int panelHeight=fixedHeight+storageHeight+bagHeight;
        float lift=Math.Clamp((GetDimensions().Height-panelHeight)/2-4,0,16);
        if (storage.Height.Pixels==storageHeight && matches.Height.Pixels==bagHeight && panel.Top.Pixels==-lift) return;
        panel.Top.Set(-lift,0);
        mainFrame.Top.Set(-lift,0);
        storage.Height.Set(storageHeight,0);
        equipment.Top.Set(HeaderHeight+storageHeight+SectionGap,0);
        float bagTop=equipment.Top.Pixels+equipmentHeight+SectionGap;
        matches.Top.Set(bagTop+30,0);
        scrollbar.Top.Set(bagTop+30,0);
        matches.Height.Set(bagHeight,0);
        scrollbar.Height.Set(bagHeight,0);
        if (bagHeight >= ResultRow.BagHeight) scrollbar.Remove();
        else if (scrollbar.Parent == null) panel.Append(scrollbar);
        panel.Height.Set(panelHeight,0);
        mainFrame.Height.Set(panel.Height.Pixels,0);
        mainFrame.Recalculate();
        panel.Recalculate();
    }

    protected override void DrawChildren(SpriteBatch batch)
    {
        var frame = mainFrame.GetDimensions().ToRectangle();
        TerminalSkin.Panel(batch,frame);
        var grid = storage.GetDimensions().ToRectangle();
        var header = ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/TerminalHeader").Value;
        batch.Draw(header,new Rectangle(frame.X,frame.Y,frame.Width,grid.Y-frame.Y),Color.White);
        var title = new Vector2(grid.X,frame.Y+10);
        int color=TeamClient.Open?TeamClient.Team:SingleTerminal.Color;
        if(color>0){
            string team=TerminalText.Get(new[]{"","红队","绿队","蓝队","黄队","粉队"}[color]);
            TerminalFont.Draw(batch,team,title,Color.Lerp(Main.teamColor[color],Color.Black,.32f),.85f);
            title.X+=TerminalFont.Measure(team,.85f).X;
        }
        TerminalSkin.Label(batch,TerminalText.Get(TeamClient.Open && TeamClient.Team==0?"个人终端":"终端"),title);
        if (queryLabel.Parent != null)
        {
            if(!ModalSettingsOpen && !OrdersOpen)ReadSearchInput();
            var field = queryLabel.GetDimensions().ToRectangle();
            int edge = Math.Max(1, (int)Math.Round(field.Height / 20f));
            var bevel = field;
            void Paint(Rectangle rect, Color color) => batch.Draw(TextureAssets.MagicPixel.Value, rect, color);
            Paint(field, Color.Black);
            bevel.Inflate(-edge, -edge);
            Paint(bevel, new Color(86,86,85));
            Paint(new Rectangle(bevel.X,bevel.Y,bevel.Width,edge), new Color(172,172,172));
            Paint(new Rectangle(bevel.X,bevel.Y,edge,bevel.Height-edge), new Color(172,172,172));
            bevel.Inflate(-edge, -edge);
            Paint(bevel, Color.Black);
            selection.Draw(batch,field,query,editing,Main.GameUpdateCount-caretStart);
        }
        var bag = matches.GetDimensions().ToRectangle();
        TerminalSkin.Label(batch,TerminalText.Get("背包"),new Vector2(grid.X,bag.Y-30));
        int side = ResultRow.SlotSpacing;
        foreach (int x in new[]{bag.X, bag.Right-side})
        {
            bool coins=(x==bag.X)!=Preferences.SwapSideColumns;
            if (!Preferences.ShowsColumn(coins)) continue;
            TerminalSkin.Panel(batch,new Rectangle(x-3,bag.Y-24,side+6,Math.Min(bag.Height,4*side)+27));
            TerminalSkin.Label(batch,TerminalText.Get(coins ? "钱币" : "弹药"),new Vector2(x+2,bag.Y-24));
        }
        sweep.Frame(bag,!ModalSettingsOpen && !OrdersOpen);
        base.DrawChildren(batch);
        // UIList restores the default filtered batch after clipping the backpack.
        TerminalSkin.BeginPixelBatch(batch);
        float cardScale = Math.Min(2f,(GetDimensions().ToRectangle().Right-frame.Right)/23f);
        if (cardScale > 0)
            Main.LocalPlayer.GetModPlayer<TerminalMagnet>().DrawDock(batch,
                new Vector2(frame.Right-4*cardScale,frame.Top),cardScale);
        // Paint the two top bevel bands on one pixel grid across all three adjoining panels.
        int toolbarLeft=frame.Left-(int)MathF.Round(21*frame.Width/195f);
        int dockRight=frame.Right+(int)MathF.Round(23*Math.Max(0,cardScale));
        var pixel=TextureAssets.MagicPixel.Value;
        batch.Draw(pixel,new Rectangle(toolbarLeft,frame.Top,dockRight-toolbarLeft,2),TerminalSkin.Ink);
        batch.Draw(pixel,new Rectangle(toolbarLeft+2,frame.Top+2,dockRight-toolbarLeft-4,3),new Color(242,242,242));
        batch.Draw(pixel,new Rectangle(frame.Left-1,frame.Top+5,10,3),new Color(203,204,212));
        int hammerSize=Math.Min(38,grid.Y-frame.Top);
        var hammer=new Rectangle(frame.Right-hammerSize-6,Math.Max(GetDimensions().ToRectangle().Top,frame.Top-4),hammerSize,hammerSize);
        if (statusPanel.DrawButton(batch,hammer)) {
            OpenSettings(false);
            statusOpen=true;
        }
        var bounds = matches.GetDimensions();
        var origin = bounds.Position() + new Vector2(bounds.Width * ResultRow.BackpackRightFraction - 116, -30);
        var stock = Main.LocalPlayer.GetModPlayer<TerminalStorage>();
        bool ActionButton(int offset,string icon,string title) => EquipmentPane.IconButton(batch,
            origin+new Vector2(offset,0),ModContent.GetInstance<ProbeMod>().Assets.Request<Texture2D>("Assets/InventoryActions/"+icon).Value,
            null,title,includesFrame:true);
        if (ActionButton(0,"organize",TerminalText.Get("整理背包物品")))
        {
            ItemSorting.SortInventory();
            Recipe.FindRecipes();
        }
        if (ActionButton(29,"deposit",TerminalText.Get("全部存入"))) stock.DepositBackpack(false);
        if (ActionButton(58,"quick",TerminalText.Get("快速堆叠"))) stock.DepositBackpack(true);
        if (ActionButton(87,"restock",TerminalText.Get("补货"))) stock.Restock();
    }

    public override void OnActivate()
    {
        SettingsOpen = false;
        sweep.Reset();
        query = Preferences.Remember ? Preferences.CurrentQuery : "";
        Preferences.CurrentQuery = query;
        storage.SetSearch(query);
        if (Preferences.JeiActive && Preferences.ClearJei) ModContent.GetInstance<TerminalSystem>().WriteJei?.Invoke("");
        ApplyProvider();
        SetSearchFocus(!Preferences.JeiActive && Preferences.AutoFocus);
        if (editing) { caretStart=Main.GameUpdateCount; Main.clrInput(); }
        equipment.ResetView();
        storage.ResetView();
    }

    private string lastJei = "";
    private void ApplyProvider()
    {
        var bridge=ModContent.GetInstance<TerminalSystem>();
        string current=bridge.ReadJei?.Invoke() ?? "";
        if (Preferences.JeiActive)
        {
            if (queryLabel.Parent != null) queryLabel.Remove();
            SetSearchFocus(false);
            if (current!=lastJei) storage.ResetView();
        }
        else
        {
            if (queryLabel.Parent == null) {panel.Append(queryLabel);panel.Recalculate();}
            if (Preferences.Sync && Preferences.JeiAvailable && current!=lastJei)
            {
                query=current;Preferences.CurrentQuery=query;
                storage.SetSearch(query);
            }
        }
        lastJei=current;
    }

    public override void Update(GameTime gameTime)
    {
        ApplyProvider();
        UpdateLayout();
        base.Update(gameTime);
        if (panel.ContainsPoint(Main.MouseScreen))
        {
            Main.LocalPlayer.mouseInterface = true;
            PlayerInput.LockVanillaMouseScroll("AEBeyondDimensions");
        }
        if(editing)PlayerInput.WritingText=true;
    }

    private readonly SearchSelection selection=new();
    private bool searchMouseDown;
    private ulong lastSearchClick;
    private Point lastSearchPoint;
    private void ReadSearchInput()
    {
        bool press=Main.mouseLeft && !searchMouseDown && !PlayerInput.IgnoreMouseInterface;
        if (press)
        {
            SetSearchFocus(queryLabel.Parent!=null && queryLabel.ContainsPoint(Main.MouseScreen));
            if (editing)
            {
                equipment.EndOrderTextInput();
                caretStart=Main.GameUpdateCount;
                Main.clrInput();
                Main.mouseLeftRelease=false;
            }
        }
        if(editing){
            selection.Mouse(query,queryLabel.GetDimensions().ToRectangle(),press,Main.mouseLeft,ItemSlot.ShiftInUse);
            if(press){
                var point=Main.MouseScreen.ToPoint();
                if(lastSearchClick!=0 && Main.GameUpdateCount-lastSearchClick<24 &&
                    Math.Abs(point.X-lastSearchPoint.X)<4 && Math.Abs(point.Y-lastSearchPoint.Y)<4)
                    selection.SelectAll(query);
                lastSearchClick=Main.GameUpdateCount;lastSearchPoint=point;
            }
        }
        searchMouseDown=Main.mouseLeft;
        if (Main.mouseRight && queryLabel.ContainsPoint(Main.MouseScreen) && query.Length!=0) ApplySearch("");
        if (TerminalGuide.Instance.ItemShortcutDown) SetSearchFocus(false);
        if (!editing) return;
        if (equipment.EditingOrderQuantity) { SetSearchFocus(false); return; }

        PlayerInput.WritingText = true;
        Main.instance.HandleIME();
        var updated = selection.Read(query);
        if(updated!=query)ApplySearch(updated);
        if (Main.inputTextEnter || Main.inputTextEscape) SetSearchFocus(false);
    }

    private void ApplySearch(string updated)
    {
        caretStart=Main.GameUpdateCount;
        query = updated;
        if (Preferences.Sync && Preferences.JeiAvailable)
        {
            ModContent.GetInstance<TerminalSystem>().WriteJei?.Invoke(query);
            lastJei=query;
        }
        Preferences.CurrentQuery = query;
        storage.SetSearch(query);
    }

    private readonly InventorySweep sweep=new();

}

internal sealed class ResultRow : UIElement
{
    internal const int ColumnCount = 10;
    internal const int SlotSpacing = StoragePane.Step;
    internal const int BagHeight = SlotSpacing * 5 + 8;
    internal const int FrameWidth = ColumnCount * SlotSpacing + 36;
    private const int SideWidth = SlotSpacing + 4;
    internal const int FullWidth = FrameWidth + SideWidth * 2;
    internal const float BackpackLeftFraction = (SideWidth + 12f) / FullWidth;
    internal const float BackpackRightFraction = BackpackLeftFraction + (float)(ColumnCount * SlotSpacing) / FullWidth;
    private readonly int[] indices;
    private readonly UIList list;
    private readonly EquipmentPane equipment;

    private readonly InventorySweep sweep;
    public ResultRow(int[] indices, UIList list, EquipmentPane equipment,InventorySweep sweep)
    {
        OverrideSamplerState = SamplerState.PointClamp;
        this.sweep=sweep;
        this.indices = indices;
        this.list = list;
        this.equipment = equipment;
        Width.Set(0, 1);
        Height.Set(SlotSpacing + (indices[0] == 0 ? 8 : 0), 0);
    }

    private void DrawSideSlot(SpriteBatch batch, Vector2 position, Player player, int context, int index)
    {
        bool trash = context == ItemSlot.Context.TrashItem;
        bool transferred=!trash && sweep.Visit(player,index,position,52f*Main.inventoryScale);
        if (list.ContainsPoint(Main.MouseScreen) && EquipmentPane.IsHovered(position,52f*Main.inventoryScale))
        {
            player.mouseInterface = true;
            int before = TerminalCursor.HeldCount;
            if (!transferred && trash)
            {
                ItemSlot.Handle(ref player.trashItem,context);
                if (TerminalCursor.HeldCount > before) TerminalCursor.RememberTrash(player,before);
            }
            else if (!transferred)
            {
                if(!player.GetModPlayer<TerminalStorage>().HandleMatchingClick(index)) ItemSlot.Handle(player.inventory,context,index);
                if (TerminalCursor.HeldCount > before) TerminalCursor.RememberSlot(player.inventory,index,before);
            }
            TerminalTooltip.ShowItem(trash ? player.trashItem : player.inventory[index]);
        }
        if (trash) TerminalSkin.Item(batch,player.trashItem,context,position,Color.White);
        else DrawInventoryCell(batch,player.inventory,context,index,position);
    }

    private static void DrawInventoryCell(SpriteBatch batch, Item[] items, int context, int index, Vector2 position)
    {
        TerminalSkin.Item(batch,items[index],context,position,Color.White);
    }
    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        var bounds = GetDimensions();
        var origin = bounds.Position() + new Vector2(0, indices[0] == 0 ? 8 : 0);
        var priorScale = Main.inventoryScale;
        try
        {
            float fit = bounds.Width / FullWidth;
            Main.inventoryScale = SlotSpacing * fit / 52f;
            var player = Main.LocalPlayer;
            int row = indices[0] / 10 - 1;
            if (row >= 0)
            {
                var prefs=player.GetModPlayer<TerminalSearchSettings>();
                var right=origin+new Vector2(bounds.Width-52f*Main.inventoryScale,0);
                if (prefs.ShowsColumn(true))
                    DrawSideSlot(spriteBatch,prefs.SwapSideColumns ? right : origin,player,ItemSlot.Context.InventoryCoin,50+row);
                if (prefs.ShowsColumn(false))
                    DrawSideSlot(spriteBatch,prefs.SwapSideColumns ? origin : right,player,ItemSlot.Context.InventoryAmmo,54+row);
            }
            else {
                TerminalSkin.Panel(spriteBatch,new Rectangle((int)origin.X-3,(int)origin.Y-3,SlotSpacing+6,SlotSpacing+6));
                DrawSideSlot(spriteBatch, origin, player, ItemSlot.Context.TrashItem, 0);
                if (player.trashItem.IsAir)
                {
                    var trash = ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/TrashGlyph").Value;
                    spriteBatch.Draw(trash,new Rectangle((int)origin.X+8,(int)origin.Y+8,28,28),Color.White);
                }
            }
            var backpack = origin + new Vector2(bounds.Width * BackpackLeftFraction, 0);
            for (var column = 0; column < indices.Length; column++)
            {
                var position = backpack + new Vector2(column * SlotSpacing * fit, 0);
                int index = indices[column];
                bool specialized=!player.inventory[index].favorited && Main.mouseLeftRelease &&
                    EquipmentPane.IsHovered(position,52f*Main.inventoryScale) &&
                    (equipment.WantsTransfer && player.GetModPlayer<TerminalReforgePlayer>().CanMove(player.inventory[index]) ||
                     ItemSlot.ShiftInUse && TerminalWorkArea.IsStationOutput(player.inventory[index]));
                bool transferred=sweep.Visit(player,index,position,52f*Main.inventoryScale,specialized);
                if (list.ContainsPoint(Main.MouseScreen) && EquipmentPane.IsHovered(position, 52f * Main.inventoryScale))
                {
                    player.mouseInterface = true;
                    ItemSlot.OverrideHover(player.inventory, ItemSlot.Context.InventoryItem, index);
                    if(transferred){ /* gesture already handled the slot */ }
                    else if (Main.cursorOverride == Terraria.ID.CursorOverrideID.FavoriteStar || ItemSlot.ControlInUse && Main.cursorOverride == Terraria.ID.CursorOverrideID.TrashCan)
                        ItemSlot.Handle(player.inventory, ItemSlot.Context.InventoryItem, index);
                    else if (player.GetModPlayer<TerminalStorage>().HandleMatchingClick(index)) { }
                    else if (Main.mouseItem.IsAir && equipment.TryShiftStation(player.inventory[index], index))
                        Main.mouseLeftRelease=false;
                    else if (Main.mouseItem.IsAir && equipment.WantsTransfer && player.GetModPlayer<TerminalReforgePlayer>().CanMove(player.inventory[index]))
                    {
                        player.GetModPlayer<TerminalReforgePlayer>().TryTransferInventory(index);
                        Main.mouseLeftRelease = false;
                    }
                    else if (Main.mouseItem.IsAir && ItemSlot.ShiftInUse && Main.mouseLeft && Main.mouseLeftRelease)
                    {
                        if (!PlayerLoader.ShiftClickSlot(player,player.inventory,ItemSlot.Context.InventoryItem,index))
                            player.GetModPlayer<TerminalStorage>().Deposit(index);
                        Main.mouseLeftRelease=false;
                    }
                    else
                    {
                        var held = Main.mouseItem;
                        int previous = TerminalCursor.HeldCount;
                        ItemSlot.Handle(player.inventory, ItemSlot.Context.InventoryItem, index);
                        if (!Main.mouseItem.IsAir && (!ReferenceEquals(held,Main.mouseItem) || TerminalCursor.HeldCount > previous))
                            TerminalCursor.RememberSlot(player.inventory,index,ReferenceEquals(held,Main.mouseItem) ? previous : 0);
                        if (ItemSlot.ShiftInUse)
                            Main.cursorOverride = player.inventory[index].IsAir || player.inventory[index].favorited
                                ? -1 : Terraria.ID.CursorOverrideID.InventoryToChest;
                    }
                    if (Main.cursorOverride != Terraria.ID.CursorOverrideID.FavoriteStar && Main.cursorOverride != Terraria.ID.CursorOverrideID.TrashCan)
                        equipment.ShowTransferCursor(player.inventory[index]);
                    TerminalTooltip.ShowItem(player.inventory[index]);
                }
                DrawInventoryCell(spriteBatch, player.inventory, ItemSlot.Context.InventoryItem, index, position);
            }
        }
        finally
        {
            Main.inventoryScale = priorScale;
        }
    }
}
