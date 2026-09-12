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
            int windowRadius = WindowRadius)
        {
            if (held == null) return false;
            if (!InWindow(cx, cz, anchorCx, anchorCz, windowRadius)) return false;

            long key = Key(cx, cz);

            // The column the anchor stands in. Letting it go would unload the anchor itself, which
            // is how it stops being able to do anything at all - including letting go of the rest.
            if (key == Key(anchorCx, anchorCz)) return false;

            if (!held.Remove(key)) held.Add(key);

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
            int windowRadius = WindowRadius)
        {
            HashSet<long> clean = JustTheAnchor(anchorCx, anchorCz);
            if (wanted == null) return clean;

            foreach (long key in wanted)
            {
                (int x, int z) = Of(key);

                if (InWindow(x, z, anchorCx, anchorCz, windowRadius)) clean.Add(key);
            }

            return clean;
        }
    }
}
