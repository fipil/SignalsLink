using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace SignalsLink.src.signals.testchest;

public sealed class GuiDialogRecycler : GuiDialog
{
    public override string ToggleKeyCombinationCode => null;
    public GuiDialogRecycler(ICoreClientAPI api, int seconds, bool half, Action<int,bool> save) : base(api)
    {
        int selectedSeconds = seconds; bool selectedHalf = half;
        var intervalLabel = ElementBounds.Fixed(0,35,330,22);
        var interval = ElementBounds.Fixed(0,62,330,30);
        var thresholdLabel = ElementBounds.Fixed(0,110,330,22);
        var threshold = ElementBounds.Fixed(0,137,330,30);
        var cancel = ElementBounds.Fixed(0,195,150,26);
        var confirm = ElementBounds.Fixed(180,195,150,26);
        var body = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        body.BothSizing = ElementSizing.FitToChildren;
        body.WithChildren(intervalLabel,interval,thresholdLabel,threshold,cancel,confirm);
        int[] values = {0,1,2,5,10};
        SingleComposer = api.Gui.CreateCompo("signalslink-recycler",ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(body)
            .AddDialogTitleBar(Lang.Get("signalslink:recycler-settings"),()=>TryClose())
            .BeginChildElements(body)
            .AddStaticText(Lang.Get("signalslink:recycler-interval"),CairoFont.WhiteSmallText(),intervalLabel)
            .AddDropDown(values.Select(v=>v.ToString()).ToArray(),
                values.Select(v=>v==0?Lang.Get("signalslink:recycler-immediate"):v+" s").ToArray(),
                Math.Max(0,Array.IndexOf(values,seconds)),(code,on)=>{if(on) selectedSeconds=int.Parse(code);},interval,"interval")
            .AddStaticText(Lang.Get("signalslink:recycler-trigger"),CairoFont.WhiteSmallText(),thresholdLabel)
            .AddDropDown(new[]{"full","half"},new[]{Lang.Get("signalslink:recycler-full"),Lang.Get("signalslink:recycler-half")},
                half?1:0,(code,on)=>{if(on) selectedHalf=code=="half";},threshold,"threshold")
            .AddSmallButton(Lang.Get("signalslink:recycler-cancel"),()=>{TryClose();return true;},cancel)
            .AddSmallButton(Lang.Get("signalslink:recycler-save"),()=>{save(selectedSeconds,selectedHalf);TryClose();return true;},confirm)
            .EndChildElements().Compose();
    }
}
