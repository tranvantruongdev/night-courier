using NightCourier.Core;

namespace NightCourier.UI
{
    /// <summary>Card text for each item, in English (also the localization key).</summary>
    public static class ItemText
    {
        public static string Name(ItemKind item) => item switch
        {
            ItemKind.Headlight => "Headlight",
            ItemKind.SpokeCards => "Spoke Cards",
            ItemKind.Bell => "Bell",
            ItemKind.ChainWhip => "Chain Whip",
            ItemKind.TyreSpikes => "Tyre Spikes",
            ItemKind.BigBasket => "Big Basket",
            ItemKind.GearRatio => "Gear Ratio",
            ItemKind.Helmet => "Helmet",
            ItemKind.LighterFrame => "Lighter Frame",
            ItemKind.BetterBrakes => "Better Brakes",
            ItemKind.EnergyGel => "Energy Gel",
            _ => item.ToString(),
        };

        public static string Line(ItemKind item) => item switch
        {
            ItemKind.Headlight => "Burns drones ahead. Wider and hotter.",
            ItemKind.SpokeCards => "Blades orbit the bike. One more blade.",
            ItemKind.Bell => "A shockwave that knocks drones back.",
            ItemKind.ChainWhip => "Lashes the side you turn toward.",
            ItemKind.TyreSpikes => "Leaves a burning trail. Longer at speed.",
            ItemKind.BigBasket => "Pulls parcels in from 25% further.",
            ItemKind.GearRatio => "Weapons recharge 8% faster.",
            ItemKind.Helmet => "Drone hits hurt 8% less.",
            ItemKind.LighterFrame => "6% faster top and cruise speed.",
            ItemKind.BetterBrakes => "Keep the speed bonus while braking.",
            ItemKind.EnergyGel => "Regain 0.4 HP a second.",
            _ => "",
        };
    }
}
