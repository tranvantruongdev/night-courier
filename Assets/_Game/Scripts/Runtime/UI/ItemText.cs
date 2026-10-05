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
            ItemKind.BigBasket => "Big Basket",
            ItemKind.GearRatio => "Gear Ratio",
            ItemKind.Helmet => "Helmet",
            _ => item.ToString(),
        };

        public static string Line(ItemKind item) => item switch
        {
            ItemKind.Headlight => "Burns drones ahead. Wider and hotter.",
            ItemKind.SpokeCards => "Blades orbit the bike. One more blade.",
            ItemKind.Bell => "A shockwave that knocks drones back.",
            ItemKind.BigBasket => "Pulls parcels in from 25% further.",
            ItemKind.GearRatio => "Weapons recharge 8% faster.",
            ItemKind.Helmet => "Drone hits hurt 8% less.",
            _ => "",
        };
    }
}
