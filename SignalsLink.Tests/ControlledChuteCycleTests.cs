using SignalsLink.Testing;
namespace SignalsLink.Tests;

public class ControlledChuteCycleTests
{
    private static void Hold(ControlledChuteCycle c,int source,int target,int input,int output,long supplied=8,long received=0)
    {
        for(int i=0;i<5;i++) c.Observe(1,source,target,input,output,supplied,received,false);
    }
    private static ControlledChuteCycle Filled()
    {
        var c=new ControlledChuteCycle(); c.Reset(); Hold(c,0,0,15,0,0,0);
        Assert.Equal(1,c.Phase); c.Observe(.1,8,0,0,0,8,0,false); Assert.Equal(2,c.Phase); return c;
    }
    [Fact]
    public void Complete_cycle_requires_both_stable_outputs_and_conserved_delivery()
    {
        var c=Filled(); Hold(c,8,0,0,0); Assert.Equal(3,c.Phase);
        c.Observe(.1,0,8,15,9,8,0,false); Assert.Equal(4,c.Phase);
        Hold(c,0,8,0,9); Assert.Equal(5,c.Phase);
        c.Observe(.1,0,0,0,0,8,8,false); Assert.Equal(6,c.Phase);
        bool complete=false; for(int i=0;i<5;i++) complete |= c.Observe(1,0,0,0,0,8,8,false).Complete;
        Assert.True(complete); Assert.Equal(1,c.Phase);
    }
    [Fact]
    public void Stuck_output_cannot_pass_empty_target_even_if_source_has_eight()
    {
        var c=Filled(); Hold(c,8,0,0,9); Assert.Equal(2,c.Phase);
        var result=c.Observe(61,8,0,0,9,8,0,false);
        Assert.NotNull(result.Failure); Assert.False(result.Complete); Assert.Equal(0,c.Phase);
    }
    [Fact]
    public void Large_single_time_step_does_not_prove_a_stable_output()
    {
        var c=Filled(); c.Observe(10,8,0,0,0,8,0,false); Assert.Equal(2,c.Phase);
    }
    [Fact]
    public void Missing_delivery_and_overfill_cannot_pass()
    {
        var c=Filled(); Assert.NotNull(c.Observe(61,16,0,0,0,16,0,false).Failure);
        c=Filled(); Hold(c,8,0,0,0); c.Observe(.1,0,8,15,9,8,0,false);
        Hold(c,0,8,0,9); c.Observe(.1,0,0,0,0,8,7,false);
        Assert.NotNull(c.Observe(61,0,0,0,0,8,7,false).Failure);
    }
    [Fact]
    public void Full_recycler_waits_without_claiming_success_and_resumes_when_drained()
    {
        var c=Filled(); Hold(c,8,0,0,0); c.Observe(.1,0,8,15,9,8,0,false); Hold(c,0,8,0,9);
        var result=c.Observe(61,0,8,0,9,8,0,true);
        Assert.True(result.Waiting); Assert.Null(result.Failure); Assert.False(result.Complete); Assert.Equal(5,c.Phase);
        c.Observe(.1,0,0,0,0,8,8,false); Assert.Equal(6,c.Phase);
    }
    [Fact]
    public void Reload_restarts_recovery_without_counting_partial_cycle()
    {
        var c=Filled(); c.Reset(); Assert.Equal(0,c.Phase); Assert.True(c.TestEnabled); Assert.True(c.DrainEnabled); Assert.False(c.FillEnabled);
        Assert.False(c.Observe(.1,8,0,15,0,8,0,false).Complete);
    }
}
