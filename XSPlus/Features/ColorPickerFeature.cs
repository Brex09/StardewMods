﻿namespace XSPlus.Features
{
    using System.Diagnostics.CodeAnalysis;
    using Common.Helpers;
    using Common.Models;
    using Common.Services;
    using Common.UI;
    using CommonHarmony.Services;
    using HarmonyLib;
    using Microsoft.Xna.Framework;
    using Services;
    using StardewModdingAPI;
    using StardewModdingAPI.Events;
    using StardewModdingAPI.Utilities;
    using StardewValley;
    using StardewValley.Menus;
    using StardewValley.Objects;

    /// <inheritdoc />
    internal class ColorPickerFeature : BaseFeature
    {
        // TODO: Add toggle button
        private const int Width = 58;
        private const int Height = 558;
        private readonly PerScreen<Chest> _chest = new();
        private readonly PerScreen<Chest> _fakeChest = new();
        private readonly PerScreen<HSLSlider> _hslSlider = new();
        private readonly ItemGrabMenuChangedService _itemGrabMenuChangedService;
        private readonly ItemGrabMenuConstructedService _itemGrabMenuConstructedService;
        private readonly PerScreen<ItemGrabMenu> _menu = new();
        private readonly RenderedActiveMenuService _renderedActiveMenuService;
        private readonly PerScreen<int> _screenId = new()
        {
            Value = -1,
        };
        private MixInfo _setSourceItemPatch;

        private ColorPickerFeature(
            ModConfigService modConfigService,
            ItemGrabMenuConstructedService itemGrabMenuConstructedService,
            ItemGrabMenuChangedService itemGrabMenuChangedService,
            RenderedActiveMenuService renderedActiveMenuService)
            : base("ColorPicker", modConfigService)
        {
            this._itemGrabMenuConstructedService = itemGrabMenuConstructedService;
            this._itemGrabMenuChangedService = itemGrabMenuChangedService;
            this._renderedActiveMenuService = renderedActiveMenuService;
        }

        /// <summary>
        ///     Gets or sets the instance of <see cref="ColorPickerFeature" />.
        /// </summary>
        private static ColorPickerFeature Instance { get; set; }

        /// <summary>
        ///     Returns and creates if needed an instance of the <see cref="ColorPickerFeature" /> class.
        /// </summary>
        /// <param name="serviceManager">Service manager to request shared services.</param>
        /// <returns>Returns an instance of the <see cref="ColorPickerFeature" /> class.</returns>
        public static ColorPickerFeature GetSingleton(ServiceManager serviceManager)
        {
            var modConfigService = serviceManager.RequestService<ModConfigService>();
            var itemGrabMenuConstructedService = serviceManager.RequestService<ItemGrabMenuConstructedService>();
            var itemGrabMenuChangedService = serviceManager.RequestService<ItemGrabMenuChangedService>();
            var renderedActiveMenuService = serviceManager.RequestService<RenderedActiveMenuService>();
            return ColorPickerFeature.Instance ??= new ColorPickerFeature(
                modConfigService,
                itemGrabMenuConstructedService,
                itemGrabMenuChangedService,
                renderedActiveMenuService);
        }

        /// <inheritdoc />
        public override void Activate()
        {
            // Events
            this._itemGrabMenuConstructedService.AddHandler(this.OnItemGrabMenuConstructedEvent);
            this._itemGrabMenuChangedService.AddHandler(this.OnItemGrabMenuChanged);
            this._renderedActiveMenuService.AddHandler(this.OnRenderedActiveMenu);
            Events.GameLoop.GameLaunched += this.OnGameLaunched;
            Events.Input.ButtonPressed += this.OnButtonPressed;
            Events.Input.ButtonReleased += this.OnButtonReleased;
            Events.Input.CursorMoved += this.OnCursorMoved;
            Events.Input.MouseWheelScrolled += this.OnMouseWheelScrolled;

            // Patches
            this._setSourceItemPatch = Mixin.Postfix(
                AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.setSourceItem)),
                typeof(ColorPickerFeature),
                nameof(ColorPickerFeature.ItemGrabMenu_setSourceItem_postfix));
        }

        /// <inheritdoc />
        public override void Deactivate()
        {
            // Events
            this._itemGrabMenuConstructedService.RemoveHandler(this.OnItemGrabMenuConstructedEvent);
            this._itemGrabMenuChangedService.RemoveHandler(this.OnItemGrabMenuChanged);
            this._renderedActiveMenuService.RemoveHandler(this.OnRenderedActiveMenu);
            Events.GameLoop.GameLaunched -= this.OnGameLaunched;
            Events.Input.ButtonPressed -= this.OnButtonPressed;
            Events.Input.ButtonReleased -= this.OnButtonReleased;
            Events.Input.CursorMoved -= this.OnCursorMoved;
            Events.Input.MouseWheelScrolled -= this.OnMouseWheelScrolled;

            // Patches
            Mixin.Unpatch(this._setSourceItemPatch);
        }

        [SuppressMessage("ReSharper", "SA1313", Justification = "Naming is determined by Harmony.")]
        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Naming is determined by Harmony.")]
        private static void ItemGrabMenu_setSourceItem_postfix(ItemGrabMenu __instance)
        {
            if (__instance.context is not Chest chest || !ColorPickerFeature.Instance.IsEnabledForItem(chest))
            {
                return;
            }

            __instance.chestColorPicker = null;
            __instance.discreteColorPickerCC = null;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            this._hslSlider.Value = new HSLSlider();
        }

        private void OnItemGrabMenuConstructedEvent(object sender, ItemGrabMenuEventArgs e)
        {
            if (e.ItemGrabMenu is null || e.Chest is null || !this.IsEnabledForItem(e.Chest))
            {
                return;
            }

            // Remove vanilla color picker
            e.ItemGrabMenu.chestColorPicker = null;
            e.ItemGrabMenu.discreteColorPickerCC = null;
        }

        private void OnItemGrabMenuChanged(object sender, ItemGrabMenuEventArgs e)
        {
            if (e.ItemGrabMenu is null || e.Chest is null || !this.IsEnabledForItem(e.Chest))
            {
                this._screenId.Value = -1;
                this._menu.Value = null;
                return;
            }

            this._screenId.Value = Context.ScreenId;
            this._menu.Value = e.ItemGrabMenu;
            this._chest.Value = e.Chest;
            this._fakeChest.Value = new Chest(true, e.Chest.ParentSheetIndex)
            {
                Name = e.Chest.Name,
                lidFrameCount =
                {
                    Value = e.Chest.lidFrameCount.Value,
                },
                playerChoiceColor =
                {
                    Value = e.Chest.playerChoiceColor.Value,
                },
            };

            foreach (var modData in e.Chest.modData)
            {
                this._fakeChest.Value.modData.CopyFrom(modData);
            }

            this._fakeChest.Value.resetLidFrame();

            this._hslSlider.Value.Area = new Rectangle(e.ItemGrabMenu.xPositionOnScreen + e.ItemGrabMenu.width + 96 + IClickableMenu.borderWidth / 2, e.ItemGrabMenu.yPositionOnScreen - 56 + IClickableMenu.borderWidth / 2, ColorPickerFeature.Width, ColorPickerFeature.Height);
            this._hslSlider.Value.Color = e.Chest.playerChoiceColor.Value;
        }

        private void OnRenderedActiveMenu(object sender, RenderedActiveMenuEventArgs e)
        {
            if (this._screenId.Value != Context.ScreenId || !Game1.player.showChestColorPicker)
            {
                return;
            }

            var x = this._hslSlider.Value.Area.Left;
            var y = this._hslSlider.Value.Area.Top - IClickableMenu.borderWidth / 2 - Game1.tileSize;
            this._fakeChest.Value.draw(e.SpriteBatch, x, y, 1f, true);
            this._hslSlider.Value.Draw(e.SpriteBatch);
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (this._screenId.Value != Context.ScreenId || e.Button != SButton.MouseLeft)
            {
                return;
            }

            if (Game1.player.showChestColorPicker && this._hslSlider.Value.MouseLeftButtonPressed())
            {
                Game1.playSound("coin");
                this._fakeChest.Value.playerChoiceColor.Value = this._hslSlider.Value.Color;
                return;
            }

            // Override color picker
            var point = Game1.getMousePosition(true);
            if (this._menu.Value.colorPickerToggleButton is not null && this._menu.Value.colorPickerToggleButton.containsPoint(point.X, point.Y))
            {
                Game1.player.showChestColorPicker = !Game1.player.showChestColorPicker;
                Game1.playSound("drumkit6");
                Input.Suppress(SButton.MouseLeft);
            }
        }

        private void OnButtonReleased(object sender, ButtonReleasedEventArgs e)
        {
            if (this._screenId.Value != Context.ScreenId || !Game1.player.showChestColorPicker || e.Button != SButton.MouseLeft)
            {
                return;
            }

            if (e.Button == SButton.MouseLeft && this._hslSlider.Value.MouseLeftButtonReleased())
            {
                this._fakeChest.Value.playerChoiceColor.Value = this._hslSlider.Value.Color;
                this._chest.Value.playerChoiceColor.Value = this._fakeChest.Value.playerChoiceColor.Value;
            }
        }

        private void OnCursorMoved(object sender, CursorMovedEventArgs e)
        {
            if (this._screenId.Value != Context.ScreenId || !Game1.player.showChestColorPicker)
            {
                return;
            }

            if (this._hslSlider.Value.MouseHover())
            {
                this._fakeChest.Value.playerChoiceColor.Value = this._hslSlider.Value.Color;
            }
        }

        private void OnMouseWheelScrolled(object sender, MouseWheelScrolledEventArgs e)
        {
            if (this._screenId.Value != Context.ScreenId || !Game1.player.showChestColorPicker)
            {
                return;
            }

            if (this._hslSlider.Value.MouseWheelScroll(e.Delta))
            {
                this._fakeChest.Value.playerChoiceColor.Value = this._hslSlider.Value.Color;
                this._chest.Value.playerChoiceColor.Value = this._fakeChest.Value.playerChoiceColor.Value;
            }
        }
    }
}