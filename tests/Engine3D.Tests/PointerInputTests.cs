using System.Numerics;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class PointerInputTests
{
    private static PointerInput Focused()
    {
        var input = new PointerInput();
        input.SetFocus(true);
        input.ReconcileHeld(0);
        input.Move(new(10f, 20f), new(100f, 200f)); // первое движение после активации — только baseline
        return input;
    }

    [TestMethod]
    public void Motion_accumulates_once_per_frame_and_saved_snapshot_is_immutable()
    {
        var input = Focused();
        Assert.AreEqual(Vector2.Zero, input.Snapshot(400, 300).Delta);
        input.Move(new(15f, 17f), new(5f, -3f));
        input.Move(new(22f, 19f), new(7f, 2f));
        var saved = input.Snapshot(400, 300);
        Assert.AreEqual(new Vector2(12f, -1f), saved.Delta);
        Assert.AreEqual(new Vector2(22f, 19f), saved.Position);
        input.BeginFrame();
        Assert.AreEqual(Vector2.Zero, input.Snapshot(400, 300).Delta);
        Assert.AreEqual(saved.Position, input.Snapshot(400, 300).Position);
        input.Move(new(30f, 20f), new(8f, 1f));
        Assert.AreEqual(new Vector2(12f, -1f), saved.Delta);
        Assert.AreEqual(new Vector2(22f, 19f), saved.Position);
    }

    [TestMethod]
    public void Buttons_have_independent_edges_and_fast_click_keeps_both()
    {
        var input = Focused();
        foreach (var button in Enum.GetValues<MouseButton>()) input.ButtonDown(button);
        var saved = input.Snapshot(400, 300);
        input.ButtonUp(MouseButton.Left);
        Assert.IsTrue(input.Snapshot(400, 300).WasPressed(MouseButton.Left));
        Assert.IsTrue(input.Snapshot(400, 300).WasReleased(MouseButton.Left));
        Assert.IsFalse(input.Snapshot(400, 300).IsDown(MouseButton.Left));
        input.BeginFrame();
        input.ButtonDown(MouseButton.Right); // дубликат удержания
        Assert.IsFalse(input.Snapshot(400, 300).WasPressed(MouseButton.Right));
        Assert.IsTrue(input.Snapshot(400, 300).IsDown(MouseButton.Right));
        foreach (var button in Enum.GetValues<MouseButton>())
        {
            Assert.IsTrue(saved.IsDown(button));
            Assert.IsTrue(saved.WasPressed(button));
            Assert.IsFalse(saved.WasReleased(button));
        }
    }

    [TestMethod]
    public void Focus_loss_clears_everything_and_held_buttons_require_release_and_new_press()
    {
        var input = Focused();
        input.ButtonDown(MouseButton.Left);
        input.Move(new(25f, 30f), new(15f, 10f));
        input.SetFocus(false);
        input.ButtonDown(MouseButton.Right);
        input.Move(new(200f, 200f), new(175f, 170f));
        var unfocused = input.Snapshot(400, 300);
        Assert.AreEqual(default, unfocused);
        input.SetFocus(true);
        input.ButtonDown(MouseButton.Left); // пакет активации
        input.ReconcileHeld(PointerSnapshot.Mask(MouseButton.Left));
        input.BeginFrame();
        input.ButtonDown(MouseButton.Left);
        Assert.IsFalse(input.Snapshot(400, 300).IsDown(MouseButton.Left));
        input.Move(new(250f, 250f), new(50f, 50f));
        Assert.AreEqual(Vector2.Zero, input.Snapshot(400, 300).Delta);
        input.ButtonUp(MouseButton.Left);
        Assert.IsFalse(input.Snapshot(400, 300).WasReleased(MouseButton.Left));
        input.ButtonDown(MouseButton.Left);
        Assert.IsTrue(input.Snapshot(400, 300).WasPressed(MouseButton.Left));
        input.ButtonDown(MouseButton.Right);
        Assert.IsTrue(input.Snapshot(400, 300).WasPressed(MouseButton.Right));
    }

    [TestMethod]
    public void Mode_change_discards_pending_motion_and_only_first_following_motion()
    {
        var input = Focused();
        input.Move(new(20f, 20f), new(10f, 0f));
        var saved = input.Snapshot(400, 300);
        input.ResetMotion();
        Assert.AreEqual(Vector2.Zero, input.Snapshot(400, 300).Delta);
        input.BeginFrame();
        input.BeginFrame(); // пустые кадры не снимают подавление первого реального события
        input.Move(new(1000f, -300f), new(980f, -320f));
        Assert.AreEqual(Vector2.Zero, input.Snapshot(400, 300).Delta);
        input.Move(new(1003f, -302f), new(3f, -2f));
        Assert.AreEqual(new Vector2(3f, -2f), input.Snapshot(400, 300).Delta);
        Assert.AreEqual(new Vector2(10f, 0f), saved.Delta);
    }

    [TestMethod]
    public void Position_uses_logical_client_bounds_and_updates_inside_after_resize()
    {
        var input = Focused();
        input.SynchronizePosition(new(150.5f, 70.25f));
        Assert.IsTrue(input.Snapshot(400, 300).IsInsideClient);
        Assert.IsFalse(input.Snapshot(100, 300).IsInsideClient);
        Assert.AreEqual(new Vector2(150.5f, 70.25f), input.Snapshot(400, 300).Position);
        input.Move(new(-1f, 0f), new(-151.5f, -70.25f));
        Assert.IsFalse(input.Snapshot(400, 300).IsInsideClient);
        input.SynchronizePosition(new(400f, 0f));
        Assert.IsFalse(input.Snapshot(400, 300).IsInsideClient);
        input.SynchronizePosition(Vector2.Zero);
        Assert.IsTrue(input.Snapshot(400, 300).IsInsideClient);
        Assert.IsFalse(input.Snapshot(0, 0).IsInsideClient);
    }

    [TestMethod]
    public void Default_snapshot_is_empty_and_combined_snapshot_preserves_pointer()
    {
        var empty = default(PointerSnapshot);
        Assert.AreEqual(Vector2.Zero, empty.Position);
        Assert.AreEqual(Vector2.Zero, empty.Delta);
        Assert.IsFalse(empty.IsInsideClient);
        foreach (var button in Enum.GetValues<MouseButton>())
        {
            Assert.IsFalse(empty.IsDown(button));
            Assert.IsFalse(empty.WasPressed(button));
            Assert.IsFalse(empty.WasReleased(button));
        }
        var pointer = Focused();
        pointer.ButtonDown(MouseButton.Middle);
        var keyboard = new KeyboardInput();
        keyboard.SetFocus(true);
        keyboard.ReconcileHeld(0);
        keyboard.KeyDown(InputKey.W, false);
        var saved = keyboard.WithPointer(pointer.Snapshot(400, 300));
        pointer.SetFocus(false);
        keyboard.SetFocus(false);
        Assert.IsTrue(saved.IsDown(InputKey.W));
        Assert.IsTrue(saved.Pointer.IsDown(MouseButton.Middle));
        Assert.AreEqual(empty, default(InputSnapshot).Pointer);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(3)]
    [DataRow(int.MaxValue)]
    public void Invalid_buttons_throw_in_all_queries(int value)
    {
        var button = (MouseButton)value;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(PointerSnapshot).IsDown(button));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(PointerSnapshot).WasPressed(button));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(PointerSnapshot).WasReleased(button));
    }
}
