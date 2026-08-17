using BLight.Blare.Audio.Devices;

// Spike: can Blare reach the volume control built into a display?
//
// Analog speakers on a 3.5mm jack have a purely analogue knob with no data path
// back to the PC — unreachable by any software. Displays are different: they
// expose speaker volume over DDC/CI as VCP register 0x62. This checks whether
// this machine's monitors actually answer.

var controller = new MonitorVolumeController();
var controls = controller.GetControls();

Console.WriteLine($"Found {controls.Count} physical display(s):\n");

foreach (var control in controls)
{
    if (control.SupportsVolume)
    {
        Console.WriteLine(
            $"  [OK]   {control.Description,-28} volume {control.Volume}/{control.MaximumVolume} " +
            $"({control.VolumePercent:F0}%)");
    }
    else
    {
        Console.WriteLine($"  [no]   {control.Description,-28} no DDC/CI speaker volume");
    }
}

Console.WriteLine();

var usable = controls.Where(c => c.SupportsVolume).ToList();
if (usable.Count == 0)
{
    Console.WriteLine("No display reports a controllable speaker volume.");
    Console.WriteLine("Either these monitors have no speakers, or DDC/CI is disabled in their OSD menu.");
    return;
}

Console.WriteLine($"{usable.Count} display(s) expose a speaker volume Blare could show and control.");
Console.WriteLine("Round-tripping the first one to confirm writes are honoured...");

var target = usable[0];
var original = target.VolumePercent;
var probe = original >= 50 ? original - 10 : original + 10;

if (!controller.TrySetVolumePercent(target.Description, probe))
{
    Console.WriteLine("  Write refused — this display reports volume but won't accept changes.");
    return;
}

await Task.Delay(600);

var after = controller.GetControls().First(c => c.Description == target.Description);
Console.WriteLine($"  set {probe:F0}% -> reads back {after.VolumePercent:F0}%");

controller.TrySetVolumePercent(target.Description, original);
Console.WriteLine($"  restored to {original:F0}%");

Console.WriteLine(
    Math.Abs(after.VolumePercent - probe) <= 5
        ? "\nCONFIRMED: display speaker volume is readable and writable."
        : "\nPartial: the display accepted the write but reports a different value.");
