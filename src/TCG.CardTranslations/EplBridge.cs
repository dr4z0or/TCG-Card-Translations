using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace TCG.CardTranslations
{
    // Read-only access to Enhanced Prefab Loader's card data, by reflection so that
    // this mod neither ships nor requires EPL at compile time. Any missing member
    // simply yields no data: the cards then stay in English.
    internal static class EplBridge
    {
        public const string ExtensionsType = "EnhancedPrefabLoader.API.Extensions.StreamingAssetExtensions";
        public const string ProcessingType = "EnhancedPrefabLoader.Core.LoadingPipeline.ProcessingService";
        const string RuntimeDataType = "EnhancedPrefabLoader.Core.EplRuntimeData";
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static readonly Dictionary<(Type, string), MemberInfo?> members = new Dictionary<(Type, string), MemberInfo?>();
        static Type? runtimeData;

        public static object? Get(object? target, string name, Type? staticType = null)
        {
            var type = staticType ?? target?.GetType();
            if (type == null) return null;
            if (!members.TryGetValue((type, name), out var member))
            {
                member = (MemberInfo?)type.GetProperty(name, Any) ?? type.GetField(name, Any);
                members[(type, name)] = member;
            }
            try
            {
                return member switch
                {
                    PropertyInfo p => p.GetValue(p.GetGetMethod(true)?.IsStatic == true ? null : target, null),
                    FieldInfo f => f.GetValue(f.IsStatic ? null : target),
                    _ => null,
                };
            }
            catch (TargetInvocationException) { return null; }
        }

        public static string Text(object? target, string name) => Get(target, name) as string ?? "";

        public static bool Set(object target, string name, object? value)
        {
            var property = target.GetType().GetProperty(name, Any);
            if (property?.GetSetMethod(true) != null) { property.SetValue(target, value, null); return true; }
            var field = target.GetType().GetField(name, Any);
            if (field != null) { field.SetValue(target, value); return true; }
            return false;
        }

        // EplRuntimeData.Assets.CardExpansionLibrary.CardExpansionData : expansion -> ModCardExpansionData.
        public static IDictionary? Expansions()
        {
            runtimeData ??= AccessTools.TypeByName(RuntimeDataType);
            if (runtimeData == null) return null;
            var assets = Get(null, "Assets", runtimeData);
            var library = Get(assets, "CardExpansionLibrary");
            return Get(library, "CardExpansionData") as IDictionary;
        }

        public readonly struct Card
        {
            public readonly string Bundle, Sprite;
            public readonly object? Monster;
            public Card(string bundle, string sprite, object? monster) { Bundle = bundle; Sprite = sprite; Monster = monster; }
        }

        public static List<Card> Cards(IDictionary expansions)
        {
            var result = new List<Card>();
            foreach (var expansion in expansions.Values)
            {
                if (!(Get(expansion, "Cards") is IDictionary cards)) continue;
                foreach (var card in cards.Values)
                {
                    var icon = Get(card, "Icon");
                    var sprite = Text(icon, "AssetName");
                    if (sprite.Length == 0) continue;
                    result.Add(new Card(Text(icon, "BundlePath"), sprite, Get(card, "MonsterData")));
                }
            }
            return result;
        }
    }

    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
