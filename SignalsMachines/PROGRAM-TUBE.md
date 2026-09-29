# Program tube prototype

Creative tab: **Signals Machines**. Place the crafting machine, then right-click its
recessed socket with a program tube. Right-click the installed tube with an empty
hand to remove it. The socket has eight perimeter contacts and no center contact.
The creative tab also contains two visual sample tubes.

The lower block entity stores the complete ItemStack, including its attributes.
Only the server transfers stacks. Removing a tube preserves its program; breaking
the machine drops the installed tube. The upper multiblock proxy cannot operate
the socket. Installed tubes render at half the standalone shape's size.

Portable stack attributes:

| Attribute | Purpose |
| --- | --- |
| `programId` | Stable program identifier; defaults to `blank`. |
| `programName` | Optional display name. |
| `program` | TreeAttribute containing the future circuit definition. |
| `pinCount` | Number of occupied perimeter contacts, clamped to 1–8; defaults to 8. |

The visual component arrangement is derived from `programId` and canonically
ordered program data. It is stable across reloads and shared between handheld and
installed rendering. `pinCount` uses consecutive contacts clockwise from northwest.
Shape texture paths remain relative for VS Model Creator; item/block definitions
bind them to textures bundled with this mod.

This prototype implements the item, socket, persistence and appearance. Circuit
imprinting, algorithm execution and functional Signals wiring are not implemented
yet. The Sequencer and Regulator creative samples demonstrate appearance only.

Validation without deploying to Publish:

```powershell
dotnet test ../SignalsMachines.Tests/SignalsMachines.Tests.csproj -p:SkipModPublish=true
```
