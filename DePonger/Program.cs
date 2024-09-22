using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using dnlib.DotNet.MD;

namespace DePonger
{
    internal class Program
    {
        static void Main(string[] args)
        {
            ModuleDefMD ponModule = ModuleDefMD.Load("pon.exe");

            Dictionary<int, string> stringsMap = ReadResourceDictionary<string>(ponModule, "realpongo_2");
            Dictionary<int, int> tokensMap = ReadResourceDictionary<int>(ponModule, "realpongo_1");

            foreach (var kvp in stringsMap)
                Console.WriteLine($"{kvp.Key} -> {kvp.Value}");
            Console.WriteLine("========");
            foreach (var kvp in tokensMap)
                Console.WriteLine($"{kvp.Key} -> {kvp.Value}");
            Console.WriteLine("========");

            foreach (TypeDef typedef in ponModule.GetTypes().Where(t => t.FullName.StartsWith("pon_")))
            {
                int keyFieldToken = typedef.Fields.Where(f => f.IsStatic && !f.IsPublic).Single()
                    .MDToken.ToInt32();
                int obfMethodToken = typedef.Methods.Where(f => f.IsStatic && f.IsPublic).Single()
                    .MDToken.ToInt32();

                int deobfMethodToken = tokensMap[keyFieldToken];
                IMethodDefOrRef targetMethod = ResolveMethodDefOrRef(ponModule, deobfMethodToken);

                foreach (TypeDef type in ponModule.GetTypes())
                {
                    foreach (MethodDef typeMethod in type.Methods.Where(m => m.Body != null))
                    {
                        for (int instrIndex = 0; instrIndex < typeMethod.Body.Instructions.Count; instrIndex++)
                        {
                            Instruction instr = typeMethod.Body.Instructions[instrIndex];
                            if (instr.OpCode == OpCodes.Call)
                            {
                                Instruction prevInstr = typeMethod.Body.Instructions[instrIndex - 1];
                                if (prevInstr.OpCode == OpCodes.Ldsfld &&
                                    (instr.Operand as IMethodDefOrRef).MDToken.ToInt32() == obfMethodToken)
                                {
                                    typeMethod.Body.Instructions[instrIndex].Operand = targetMethod;
                                    typeMethod.Body.Instructions.RemoveAt(instrIndex - 1);
                                    instrIndex--;
                                }
                                if (instr.Operand is MethodDef callee &&
                                    callee.Name == "pongo_POLTBZD0VO")
                                {
                                    int value = (int)typeMethod.Body.Instructions[instrIndex - 1].Operand;
                                    typeMethod.Body.Instructions[instrIndex] = new Instruction(OpCodes.Ldstr, stringsMap[value]);
                                    typeMethod.Body.Instructions.RemoveAt(instrIndex - 1);
                                    instrIndex--;
                                }
                            }
                        }
                        typeMethod.Body.UpdateInstructionOffsets();
                        typeMethod.Body.OptimizeBranches();
                        typeMethod.Body.SimplifyBranches();
                    }
                }
            }

            ModuleWriterOptions moduleWriterOption = new ModuleWriterOptions(ponModule);
            moduleWriterOption.MetadataOptions.Flags = moduleWriterOption.MetadataOptions.Flags | MetadataFlags.KeepOldMaxStack;
            moduleWriterOption.Logger = DummyLogger.NoThrowInstance;
            ponModule.Write("depon.exe", moduleWriterOption);

            Console.WriteLine("Done!");
        }

        static IMethodDefOrRef ResolveMethodDefOrRef(ModuleDefMD module, int token)
        {
            uint rid = MDToken.ToRID(token);
            if (MDToken.ToTable(token) == Table.Method)
                return module.ResolveMethod(rid);
            else
                return module.ResolveMemberRef(rid);
        }

        static Dictionary<int, T> ReadResourceDictionary<T>(ModuleDef module, string resourceName)
        {
            EmbeddedResource resource = module.Resources.Where(r => r.Name == resourceName).Single() as EmbeddedResource;
            BinaryFormatter binaryFormatter = new BinaryFormatter();
            using (Stream resourceStream = resource.CreateReader().AsStream())
                return (Dictionary<int, T>)binaryFormatter.Deserialize(resourceStream);
        }
    }
}
