using System.Reflection.Emit;
using System.Reflection.Metadata;

namespace XylemUI.Tools.FluentIcons;

/// <summary>
/// Reads IL one instruction at a time: the opcodes from <see cref="OpCodes"/>, and the size of each kind of operand (ECMA-335,
/// Partition III).
/// </summary>
internal static class OpCodeTable
{
    private static readonly OpCode[] OneByte = new OpCode[256];
    private static readonly OpCode[] TwoByte = new OpCode[256];

    static OpCodeTable()
    {
        foreach (System.Reflection.FieldInfo field in typeof(OpCodes).GetFields())
        {
            var op = (OpCode)field.GetValue(null)!;
            ushort value = (ushort)op.Value;
            if (op.Size == 1)
            {
                OneByte[value] = op;
            }
            else
            {
                TwoByte[value & 0xFF] = op;
            }
        }
    }

    /// <summary>
    /// Reads an opcode, one byte or two (0xFE and a byte).
    /// </summary>
    /// <param name="il">The reader at an instruction.</param>
    /// <returns>The opcode; the reader is at its operand.</returns>
    public static OpCode Read(ref BlobReader il)
    {
        byte first = il.ReadByte();
        return first == 0xFE ? TwoByte[il.ReadByte()] : OneByte[first];
    }

    /// <summary>
    /// Moves the reader past an opcode's operand.
    /// </summary>
    /// <param name="il">The reader at the operand.</param>
    /// <param name="op">The opcode.</param>
    public static void SkipOperand(ref BlobReader il, OpCode op)
    {
        switch (op.OperandType)
        {
            case OperandType.InlineNone:
                break;
            case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                il.Offset += 1;
                break;
            case OperandType.InlineVar:
                il.Offset += 2;
                break;
            case OperandType.InlineI8 or OperandType.InlineR:
                il.Offset += 8;
                break;
            case OperandType.InlineSwitch:
                int targets = il.ReadInt32();
                il.Offset += 4 * targets;
                break;
            default:
                il.Offset += 4;
                break;
        }
    }
}
