using Vintagestory.API.Common;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// The visual profile of one kind of line: how thick it is, how deeply it sags and what it is
    /// covered with. A sleeve has to hang noticeably straighter than a hose, because obstruction
    /// checking tests the straight chord between the anchors — a deep sag would visibly grow into
    /// the blocks below while the rule says the path is clear.
    /// </summary>
    public readonly struct LinkProfile
    {
        /// <summary>Half-width of the square cross-section, in block units (the line is 2x this wide).</summary>
        public readonly float Thickness;

        /// <summary>Catenary "a": smaller sags deeper. A wire uses 2.0.</summary>
        public readonly float CatenaryA;

        public readonly AssetLocation Texture;

        /// <summary>
        /// How many times the texture repeats along one block of length. It cannot be one shared
        /// constant: a face is 2 * Thickness wide and always spans the same 3/16 slice of the
        /// texture, so the across-the-line texel density is fixed by the thickness. To keep the
        /// weave square, this has to fall as the line gets thicker — a sleeve is roughly three
        /// times thicker than a hose and so repeats roughly three times less often.
        /// </summary>
        public readonly float TextureRepeatsPerBlock;

        /// <summary>
        /// How much of the texture's height one face spans, i.e. the density ACROSS the line.
        /// Raising it and <see cref="TextureRepeatsPerBlock"/> by the same factor makes the weave
        /// finer without changing its aspect ratio; raising only one of them stretches it.
        /// </summary>
        public readonly float TextureVSpan;

        public LinkProfile(float thickness, float catenaryA, float textureRepeatsPerBlock, float textureVSpan, AssetLocation texture)
        {
            Thickness = thickness;
            CatenaryA = catenaryA;
            TextureRepeatsPerBlock = textureRepeatsPerBlock;
            TextureVSpan = textureVSpan;
            Texture = texture;
        }

        /// <summary>Leather, thin, deep sag.</summary>
        public static readonly LinkProfile Hose =
            new LinkProfile(0.04f, 0.5f, 2.0f, 3f / 16f, new AssetLocation("signalslink:block/leather.png"));

        /// <summary>
        /// Canvas, thick, shallow sag. The damper's mouth is a 4x4 hole in the 16x16 shape grid,
        /// i.e. 0.25 blocks across — hence a half-thickness of 0.125. Both texture figures are
        /// double the aspect-preserving baseline, which halves the texel and so shows the weave at
        /// twice the detail: one texel of the coarser mapping is now a 2x2 square of them.
        /// </summary>
        public static readonly LinkProfile Sleeve =
            new LinkProfile(0.125f, 1.5f, 1.28f, 6f / 16f, new AssetLocation("game:block/linen.png"));

        public static LinkProfile For(byte kind) => kind == LinkKind.Sleeve ? Sleeve : Hose;
    }
}
