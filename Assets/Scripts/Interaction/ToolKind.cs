// =============================================================================
// ToolKind
// -----------------------------------------------------------------------------
// Purpose : The list of tools on the desk, and the words shown for each.
// Idea    : Every tool is only a way of changing FIELDS (moisture, temperature,
//           nutrients, terrain) or of starting an agent; none of them touches a
//           creature directly. The simulation then does whatever follows.
//   WateringCan    adds moisture              (continuous while held)
//   CompostScoop   adds nutrients + organic   (continuous)
//   HeatLamp       raises temperature         (continuous)
//   FrostSpray     lowers temperature         (continuous)
//   SporeDish      starts fungal agents       (one burst per click)
//   Meteor         digs a crater, kills life  (one impact per click, with a cooldown)
//   SeedJar        will plant a tree          (arrives with the tree system)
//   BoidCage       will release a flock       (arrives with the flock system)
//   FieldLens      reads the ground under the cursor; overlays the temperature while held
// =============================================================================

namespace Terrarium.Interaction
{
    /// <summary>The tools that sit on the desk.</summary>
    public enum ToolKind
    {
        /// <summary>Adds moisture.</summary>
        WateringCan,

        /// <summary>Adds nutrients and organic matter.</summary>
        CompostScoop,

        /// <summary>Plants a tree (once trees exist).</summary>
        SeedJar,

        /// <summary>Starts fungal agents.</summary>
        SporeDish,

        /// <summary>Releases a flock (once flocks exist).</summary>
        BoidCage,

        /// <summary>Raises temperature.</summary>
        HeatLamp,

        /// <summary>Lowers temperature.</summary>
        FrostSpray,

        /// <summary>Digs a crater and kills life in it.</summary>
        Meteor,

        /// <summary>Inspects the ground under the cursor.</summary>
        FieldLens,
    }

    /// <summary>Display text for each tool.</summary>
    public static class ToolInfo
    {
        /// <summary>Name shown for a tool.</summary>
        public static string Name(ToolKind kind)
        {
            switch (kind)
            {
                case ToolKind.WateringCan: return "Watering can";
                case ToolKind.CompostScoop: return "Compost scoop";
                case ToolKind.SeedJar: return "Seed jar";
                case ToolKind.SporeDish: return "Spore dish";
                case ToolKind.BoidCage: return "Bird cage";
                case ToolKind.HeatLamp: return "Heat lamp";
                case ToolKind.FrostSpray: return "Frost spray";
                case ToolKind.Meteor: return "Meteor";
                case ToolKind.FieldLens: return "Field lens";
                default: return kind.ToString();
            }
        }

        /// <summary>How to use a tool, shown at the edge of the screen while it is held.</summary>
        public static string Hint(ToolKind kind)
        {
            switch (kind)
            {
                case ToolKind.WateringCan: return "hold the left button over the planet to rain";
                case ToolKind.CompostScoop: return "hold the left button over the planet to spread compost";
                case ToolKind.SeedJar: return "click to drop a seed (trees arrive in a later phase)";
                case ToolKind.SporeDish: return "click to scatter fungal spores";
                case ToolKind.BoidCage: return "click to release a flock (flocks arrive in a later phase)";
                case ToolKind.HeatLamp: return "hold the left button to warm the ground";
                case ToolKind.FrostSpray: return "hold the left button to chill the ground";
                case ToolKind.Meteor: return "click to strike the planet";
                case ToolKind.FieldLens: return "hover to read the ground; hold the left button to see temperature";
                default: return string.Empty;
            }
        }

        /// <summary>True for tools that act every frame while the button is held.</summary>
        public static bool IsContinuous(ToolKind kind)
        {
            return kind == ToolKind.WateringCan || kind == ToolKind.CompostScoop || kind == ToolKind.HeatLamp || kind == ToolKind.FrostSpray;
        }
    }
}
