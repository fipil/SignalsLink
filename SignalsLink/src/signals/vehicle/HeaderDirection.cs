using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.vehicle
{
    /// <summary>
    /// A direction in a section header: north, south, east, west - or just the first letter - and
    /// optionally how many blocks away, written against the word: <c>north5</c>.
    ///
    /// Against the word, not after it, because <c>train north 5</c> would be arguing with the
    /// wagon number over the same token.
    /// </summary>
    public static class HeaderDirection
    {
        /// <summary>The direction, or null when the token is not one. Up and down never are.</summary>
        public static BlockFacing TryParse(string token, out int? steps)
        {
            steps = null;
            if (string.IsNullOrEmpty(token)) return null;

            string word = token.ToLowerInvariant();
            int digits = word.Length;

            while (digits > 0 && char.IsDigit(word[digits - 1])) digits--;

            if (digits < word.Length && digits > 0)
            {
                if (!int.TryParse(word.Substring(digits), out int parsed)) return null;

                steps = parsed;
                word = word.Substring(0, digits);
            }

            switch (word)
            {
                case "n": return BlockFacing.NORTH;
                case "s": return BlockFacing.SOUTH;
                case "e": return BlockFacing.EAST;
                case "w": return BlockFacing.WEST;
            }

            BlockFacing facing = BlockFacing.FromCode(word);

            return facing == BlockFacing.UP || facing == BlockFacing.DOWN ? null : facing;
        }
    }
}
