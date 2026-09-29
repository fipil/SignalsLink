using System.Collections.Generic;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// Which chunk columns an anchor may hold, and which it does hold.
    ///
    /// Pure arithmetic on column coordinates, with no world behind it, because everything here has
    /// to agree in three places at once: the map the player clicks on, the packet that travels to
    /// the server, and the claim the server makes. A rule that lived in the dialog could be argued
    /// with by a crafted packet; a rule that lived on the server would let the map offer squares it
    /// then refuses.
    /// </summary>
    public static class AnchorArea
    {
        /// <summary>
        /// How far the map reaches from the anchor, in columns. 7 gives the 15x15 window.
        ///
        /// The window is what keeps one anchor from holding a railway across half the world, and
        /// it does it without any rule about the selection having to be connected.
        /// </summary>
        public const int WindowRadius = 7;

        /// <summary>
        /// The most one anchor may hold, whatever the window allows.
        ///
        /// The price is the real brake, but a ceiling is worth having as well: it is the number a
        /// server owner can point at, and it stops one player from taking the whole 15x15 window
        /// before anyone works out what that costs. The live value is in the server settings; this
        /// is the fallback for tests and for a config that has not loaded.
        /// </summary>
        public const int MaxColumns = 64;

        /// <summary>One column as a single number, so a set of them is cheap to keep and to send.</summary>
        public static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        public static (int X, int Z) Of(long key) => ((int)(key >> 32), (int)(uint)key);

        /// <summary>Is this column inside the window the player is allowed to pick from?</summary>
        public static bool InWindow(int cx, int cz, int anchorCx, int anchorCz, int windowRadius = WindowRadius)
        {
            int dx = cx - anchorCx;
            int dz = cz - anchorCz;

            if (dx < 0) dx = -dx;
            if (dz < 0) dz = -dz;

            return dx <= windowRadius && dz <= windowRadius;
        }

        /// <summary>
        /// Turns a column on or off. Returns the set unchanged when the click was not allowed -
        /// outside the window, or on the anchor's own column, which is never given up.
        /// </summary>
        public static bool Toggle(ISet<long> held, int cx, int cz, int anchorCx, int anchorCz,
            int windowRadius = WindowRadius, int maxColumns = MaxColumns)
        {
            if (held == null) return false;
            if (!InWindow(cx, cz, anchorCx, anchorCz, windowRadius)) return false;

            long key = Key(cx, cz);

            // The column the anchor stands in. Letting it go would unload the anchor itself, which
            // is how it stops being able to do anything at all - including letting go of the rest.
            if (key == Key(anchorCx, anchorCz)) return false;

            if (held.Remove(key)) return true;

            // Giving one back is always allowed; taking one more is not, once the ceiling is
            // reached. Refusing the click is kinder than accepting it and trimming it away later,
            // which would look like the map losing the player's work.
            if (maxColumns > 0 && held.Count >= maxColumns) return false;

            held.Add(key);

            return true;
        }

        /// <summary>What a newly placed anchor holds: the ground it stands on, and nothing else.</summary>
        public static HashSet<long> JustTheAnchor(int anchorCx, int anchorCz)
        {
            return new HashSet<long> { Key(anchorCx, anchorCz) };
        }

        /// <summary>
        /// A filled square. Not offered in the dialog; it is how the fixed 7x7 of the first
        /// generation is read back out of an old save.
        /// </summary>
        public static HashSet<long> Square(int anchorCx, int anchorCz, int radius)
        {
            HashSet<long> columns = new HashSet<long>();

            for (int x = anchorCx - radius; x <= anchorCx + radius; x++)
            for (int z = anchorCz - radius; z <= anchorCz + radius; z++)
            {
                columns.Add(Key(x, z));
            }

            return columns;
        }

        /// <summary>
        /// A selection as it may be trusted: inside the window, and always including the anchor's
        /// own column. Applied to whatever arrives from a client, which is not to be believed.
        /// </summary>
        public static HashSet<long> Sanitise(IEnumerable<long> wanted, int anchorCx, int anchorCz,
            int windowRadius = WindowRadius, int maxColumns = MaxColumns)
        {
            HashSet<long> clean = JustTheAnchor(anchorCx, anchorCz);
            if (wanted == null) return clean;

            List<long> inside = new List<long>();

            foreach (long key in wanted)
            {
                (int x, int z) = Of(key);

                if (key != Key(anchorCx, anchorCz) && InWindow(x, z, anchorCx, anchorCz, windowRadius))
                {
                    inside.Add(key);
                }
            }

            // Nearest first, so a set that has to be cut down keeps the ground around the anchor
            // rather than an arbitrary handful. Sorted by the key as well, so two runs on the same
            // set always cut the same way - a trim that wandered would be worse than a hard refusal.
            inside.Sort((a, b) =>
            {
                int byDistance = Distance(a, anchorCx, anchorCz).CompareTo(Distance(b, anchorCx, anchorCz));

                return byDistance != 0 ? byDistance : a.CompareTo(b);
            });

            foreach (long key in inside)
            {
                if (maxColumns > 0 && clean.Count >= maxColumns) break;

                clean.Add(key);
            }

            return clean;
        }

        private static int Distance(long key, int anchorCx, int anchorCz)
        {
            (int x, int z) = Of(key);

            int dx = x - anchorCx;
            int dz = z - anchorCz;

            return dx * dx + dz * dz;
        }
    }
}
