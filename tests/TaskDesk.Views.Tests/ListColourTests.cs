namespace TaskDesk.Views.Tests;

/// <summary>A List's stored colour reads only in its exact "#RRGGBB" form.</summary>
[TestClass]
public sealed class ListColourTests
{
    [TestMethod]
    [DataRow("#107C10", (byte)0x10, (byte)0x7C, (byte)0x10)]
    [DataRow("#abcdef", (byte)0xAB, (byte)0xCD, (byte)0xEF)]
    [DataRow("#000000", (byte)0, (byte)0, (byte)0)]
    public void Six_hex_digits_after_a_hash_read_as_an_opaque_colour(string hex, byte r, byte g, byte b)
    {
        Assert.IsTrue(ListColour.TryParse(hex, out var colour));
        Assert.AreEqual(Windows.UI.Color.FromArgb(255, r, g, b), colour);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("107C10")]
    [DataRow("#107C1")]
    [DataRow("#107C100")]
    [DataRow("#10 7C1")]
    [DataRow("#12345 ")]
    [DataRow("# 12345")]
    [DataRow("#\t12345")]
    [DataRow("#12345\n")]
    [DataRow("#0x1234")]
    [DataRow("#GGGGGG")]
    public void Any_other_form_does_not_read(string? hex)
    {
        Assert.IsFalse(ListColour.TryParse(hex, out _), $"\"{hex}\" read as a colour.");
    }
}
