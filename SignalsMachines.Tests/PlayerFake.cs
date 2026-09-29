using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;

namespace System.Runtime.CompilerServices
{
    // Known to the runtime by name: a dynamic assembly carrying it may implement internal members
    // of the named assembly. Needed because IPlayer has an internal method (1.22 only).
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName) { AssemblyName = assemblyName; }
        public string AssemblyName { get; }
    }
}

namespace SignalsMachines.Tests
{
/// <summary>
/// An IPlayer that only has a hotbar slot. Neither DispatchProxy nor a plain class can implement
/// IPlayer in 1.22 (internal member), so the type is emitted with access checks to the API
/// switched off. Every other member throws, so a block that touches more is caught by the test.
/// </summary>
public static class PlayerFake
{
    private static Type playerType;

    public static IPlayer WithInventory(IPlayerInventoryManager inventory) => (IPlayer)Activator.CreateInstance(PlayerType(), inventory);

    public class InventoryProxy : DispatchProxy
    {
        public ItemSlot Slot;
        protected override object Invoke(MethodInfo method, object[] args) =>
            method.Name == "get_ActiveHotbarSlot" ? Slot : throw new NotSupportedException(method.Name);
    }

    private static Type PlayerType()
    {
        if (playerType != null) return playerType;

        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SignalsMachines.Tests.PlayerFake"), AssemblyBuilderAccess.Run);
        var ignore = typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor(new[] { typeof(string) });
        assembly.SetCustomAttribute(new CustomAttributeBuilder(ignore, new object[] { typeof(IPlayer).Assembly.GetName().Name }));

        var type = assembly.DefineDynamicModule("PlayerFake").DefineType("FakePlayer", TypeAttributes.Public | TypeAttributes.Class, typeof(object), new[] { typeof(IPlayer) });
        var inventory = type.DefineField("inventory", typeof(IPlayerInventoryManager), FieldAttributes.Private);

        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[] { typeof(IPlayerInventoryManager) });
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, inventory);
        il.Emit(OpCodes.Ret);

        const MethodAttributes explicitImpl = MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final;
        foreach (MethodInfo member in typeof(IPlayer).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var parameters = member.GetParameters().Select(p => p.ParameterType).ToArray();
            var impl = type.DefineMethod("IPlayer." + member.Name, explicitImpl, member.ReturnType, parameters);
            var body = impl.GetILGenerator();
            if (member.Name == "get_InventoryManager")
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, inventory);
            }
            else
            {
                body.Emit(OpCodes.Ldstr, member.Name);
                body.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(new[] { typeof(string) }));
                body.Emit(OpCodes.Throw);
            }
            body.Emit(OpCodes.Ret);
            type.DefineMethodOverride(impl, member);
        }

        return playerType = type.CreateType();
    }
}
}

