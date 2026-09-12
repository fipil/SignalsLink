using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SignalsLink.Tests
{
    public class TempSig
    {
        [Fact]
        public void Dump()
        {
            var lines = new System.Collections.Generic.List<string>();

            Type bag = typeof(Vintagestory.API.Common.IHeldBag);
            lines.Add("--- IHeldBag");
            lines.AddRange(bag.GetMethods().Select(m => m.ReturnType.Name + " " + m.Name + "(" +
                string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")"));

            Type att = typeof(Vintagestory.GameContent.EntityBehaviorAttachable);
            lines.Add("--- EntityBehaviorAttachable (public)");
            lines.AddRange(att.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(m => m.MemberType + " " + m.Name +
                    (m is PropertyInfo p ? " : " + p.PropertyType.Name : m is MethodInfo mi ? "(" +
                        string.Join(", ", mi.GetParameters().Select(x => x.ParameterType.Name)) + ")" : "")));

            File.WriteAllLines(@"C:\Users\fipil\AppData\Local\Temp\claude\C--Develope-VintageStory\f0a2c7ef-ee12-43e0-8fcd-3fb92568b37e\scratchpad\bag.txt", lines);
        }
    }
}
