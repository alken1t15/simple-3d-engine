using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class KeyboardInputTests
{
    [TestMethod]
    public void Physical_held_key_stays_blocked_even_if_native_repeat_is_reported_as_new_press()
    {
        var input = Focused();
        input.KeyDown(InputKey.W, false);
        input.SetFocus(false);
        input.SetFocus(true);
        for (int frame = 0; frame < 120; frame++)
        {
            input.BeginFrame();
            input.ReconcileHeld(InputSnapshot.Mask(InputKey.W));
            Assert.IsTrue(input.NeedsRearm);
            input.KeyDown(InputKey.W, false); // GLFW cache после focus loss не помнит старый press
            Assert.IsFalse(input.Snapshot.IsDown(InputKey.W));
            Assert.IsFalse(input.Snapshot.WasPressed(InputKey.W));
        }
        input.KeyUp(InputKey.W);
        input.ReconcileHeld(0);
        Assert.IsFalse(input.NeedsRearm);
        Assert.IsFalse(input.Snapshot.WasReleased(InputKey.W));
        input.KeyDown(InputKey.W, false);
        Assert.IsTrue(input.Snapshot.WasPressed(InputKey.W));
    }

    [TestMethod]
    public void Existing_numeric_values_are_preserved_and_new_keys_have_distinct_bits()
    {
        InputKey[] original = [InputKey.A, InputKey.D, InputKey.Left, InputKey.Right, InputKey.Space, InputKey.R, InputKey.Escape];
        for (int i = 0; i < original.Length; i++) Assert.AreEqual(i, (int)original[i]);
        InputKey[] added = [InputKey.W, InputKey.S, InputKey.LeftShift, InputKey.RightShift, InputKey.Enter, InputKey.Up, InputKey.Down, InputKey.Tab];
        var input = Focused();
        foreach (var key in added)
        {
            input.BeginFrame();
            input.KeyDown(key, false);
            input.KeyDown(key, true);
            Assert.IsTrue(input.Snapshot.WasPressed(key));
            input.BeginFrame();
            input.KeyDown(key, true);
            Assert.IsFalse(input.Snapshot.WasPressed(key));
            input.KeyUp(key);
            Assert.IsTrue(input.Snapshot.WasReleased(key));
        }
        Assert.AreEqual(15, Enum.GetValues<InputKey>().Select(InputSnapshot.Mask).Distinct().Count());
    }

    private static KeyboardInput Focused()
    {
        var input = new KeyboardInput();
        input.SetFocus(true);
        input.ReconcileHeld(0);
        return input;
    }

    [TestMethod]
    public void Hold_has_one_pressed_edge_and_release_has_one_released_edge()
    {
        var input = Focused();
        input.BeginFrame();
        input.KeyDown(InputKey.Space, false);
        var saved = input.Snapshot;
        Assert.IsTrue(saved.IsFocused);
        Assert.IsTrue(saved.IsDown(InputKey.Space));
        Assert.IsTrue(saved.WasPressed(InputKey.Space));
        Assert.IsFalse(saved.WasReleased(InputKey.Space));

        input.BeginFrame();
        input.KeyDown(InputKey.Space, true);
        input.KeyDown(InputKey.Space, false); // дубликат, даже без repeat flag
        Assert.IsTrue(input.Snapshot.IsDown(InputKey.Space));
        Assert.IsFalse(input.Snapshot.WasPressed(InputKey.Space));

        input.KeyUp(InputKey.Space);
        Assert.IsFalse(input.Snapshot.IsDown(InputKey.Space));
        Assert.IsTrue(input.Snapshot.WasReleased(InputKey.Space));
        input.BeginFrame();
        Assert.IsFalse(input.Snapshot.WasReleased(InputKey.Space));
        Assert.IsTrue(saved.IsDown(InputKey.Space)); // прежний снимок неизменен
        Assert.IsTrue(saved.WasPressed(InputKey.Space));
    }

    [TestMethod]
    public void Quick_press_and_release_in_one_event_batch_keeps_both_edges()
    {
        var input = Focused();
        input.KeyDown(InputKey.Space, false);
        input.KeyUp(InputKey.Space);
        Assert.IsFalse(input.Snapshot.IsDown(InputKey.Space));
        Assert.IsTrue(input.Snapshot.WasPressed(InputKey.Space));
        Assert.IsTrue(input.Snapshot.WasReleased(InputKey.Space));
    }

    [TestMethod]
    public void Focus_loss_clears_edges_and_reactivation_requires_releasing_held_keys()
    {
        var input = Focused();
        input.KeyDown(InputKey.A, false);
        input.KeyDown(InputKey.Space, false);
        input.SetFocus(false);
        input.KeyDown(InputKey.D, false);
        foreach (var key in Enum.GetValues<InputKey>())
        {
            Assert.IsFalse(input.Snapshot.IsDown(key));
            Assert.IsFalse(input.Snapshot.WasPressed(key));
            Assert.IsFalse(input.Snapshot.WasReleased(key));
        }
        Assert.IsFalse(input.Snapshot.IsFocused);

        input.SetFocus(true);
        input.ReconcileHeld(InputSnapshot.Mask(InputKey.Space));
        input.KeyDown(InputKey.Space, true);
        input.KeyDown(InputKey.Space, false);
        Assert.IsFalse(input.Snapshot.IsDown(InputKey.Space));
        input.KeyUp(InputKey.Space);
        Assert.IsFalse(input.Snapshot.WasReleased(InputKey.Space)); // не было принятого down
        input.KeyDown(InputKey.Space, false);
        Assert.IsTrue(input.Snapshot.WasPressed(InputKey.Space));
        input.KeyDown(InputKey.D, false); // незажатая при активации клавиша уже доступна
        Assert.IsTrue(input.Snapshot.IsDown(InputKey.D));
    }

    [TestMethod]
    public void Activation_batch_is_suppressed_and_unheld_keys_work_next_frame()
    {
        var input = new KeyboardInput();
        input.SetFocus(true);
        input.KeyDown(InputKey.Space, false);
        input.ReconcileHeld(InputSnapshot.Mask(InputKey.Space));
        Assert.IsFalse(input.Snapshot.WasPressed(InputKey.Space));
        input.BeginFrame();
        input.ReconcileHeld(0);
        input.KeyDown(InputKey.Space, false);
        Assert.IsTrue(input.Snapshot.WasPressed(InputKey.Space));
    }

    [TestMethod]
    public void Keys_have_independent_bits_and_default_snapshot_is_empty()
    {
        var input = Focused();
        foreach (var key in Enum.GetValues<InputKey>())
            input.KeyDown(key, false);
        input.KeyUp(InputKey.Left);
        foreach (var key in Enum.GetValues<InputKey>())
        {
            Assert.AreEqual(key != InputKey.Left, input.Snapshot.IsDown(key));
            Assert.IsTrue(input.Snapshot.WasPressed(key));
            Assert.IsFalse(default(InputSnapshot).IsDown(key));
            Assert.IsFalse(default(InputSnapshot).WasPressed(key));
            Assert.IsFalse(default(InputSnapshot).WasReleased(key));
        }
        Assert.IsFalse(default(InputSnapshot).IsFocused);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(15)]
    [DataRow(int.MaxValue)]
    public void Invalid_keys_throw_for_all_snapshot_queries(int key)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(InputSnapshot).IsDown((InputKey)key));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(InputSnapshot).WasPressed((InputKey)key));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(InputSnapshot).WasReleased((InputKey)key));
    }
}
