namespace SignalsLink.Testing;

/// <summary>Observes real inventories and signals; never advances on elapsed time alone.</summary>
public sealed class ControlledChuteCycle
{
    public int Phase { get; private set; }
    public string PhaseName => new[] { "recover", "fill", "hold-empty", "transfer", "hold-eight", "drain", "hold-drained" }[Phase];
    public bool TestEnabled => Phase is 0 or 3;
    public bool FillEnabled => Phase == 1;
    public bool DrainEnabled => Phase is 0 or 5;
    private double elapsed, stable;
    private long supplyBaseline, receiveBaseline;
    public void Reset() { Phase=0; elapsed=0; stable=-1; }
    private void Enter(int phase) { Phase=phase; elapsed=0; stable=-1; }
    public (bool Complete, string Failure, bool Waiting) Observe(double seconds, int source, int target,
        int input, int output, long supplied, long received, bool sinkFull)
    {
        seconds=Math.Max(0,seconds); elapsed+=seconds;
        bool match=Phase switch {
            0 => source==0 && target==0 && output==0,
            1 => source==8 && target==0,
            2 => source==8 && target==0 && input==0 && output==0 && supplied-supplyBaseline==8,
            3 => source==0 && target==8 && input==15,
            4 => source==0 && target==8 && input==0 && output==9,
            5 => source==0 && target==0,
            6 => source==0 && target==0 && input==0 && output==0
                && supplied-supplyBaseline==8 && received-receiveBaseline==8,
            _ => false
        };
        // First matching observation starts the hold; one large dt cannot prove a stable state.
        if(match) { if(stable<0) stable=0; else stable+=seconds; } else stable=-1;
        if(match && (Phase is 1 or 3 or 5 || stable>=3))
        {
            bool complete=Phase==6;
            if(Phase is 0 or 6) { supplyBaseline=supplied; receiveBaseline=received; }
            Enter(complete ? 1 : Phase+1);
            return (complete,null,false);
        }
        if(elapsed>60)
        {
            if(Phase is 0 or 5 && sinkFull) return (false,null,true);
            string failure=$"{PhaseName}: source={source}, target={target}, input={input}, output={output}, supplied={supplied-supplyBaseline}, received={received-receiveBaseline}";
            Enter(0); return (false,failure,false);
        }
        return (false,null,false);
    }
}
