using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Marco.Prototype;

internal static class RoomLaunchProtocolTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Console.WriteLine("PASS: " + checks + " room launch assertions"); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static async Task Run()
    {
        foreach (var role in new[] { PrototypePlayerRole.Hunter, PrototypePlayerRole.Survivor })
        {
            Check(RoomLaunchProtocol.TryParseStart(RoomLaunchProtocol.StartCommand(role), out var parsed) && parsed == role, "Role round trip");
            MatchLaunchContext.Begin(parsed);
            Check(MatchLaunchContext.IsActive && MatchLaunchContext.LocalRole == role, "Role survives scene launch");
            MatchLaunchContext.Clear();
            Check(!MatchLaunchContext.IsActive, "Leave clears prior role");
        }
        foreach (var invalid in new[] { null, "", "START Hunter", "MARCO/2 START 0", "MARCO/2 START hunter", "MARCO/2 START Survivor extra", "MARCO/2 START OtherScene", "MARCO/2 ROSTER 0:UGxheWVy" })
            Check(!RoomLaunchProtocol.TryParseStart(invalid, out _), "Reject malformed or non-start command");
        foreach (int hunterId in new[] { 0, 1, 4 })
        {
            int hunterCount = 0;
            foreach (int playerId in new[] { 0, 1, 4 })
                if (RoomLaunchProtocol.RoleFor(playerId, hunterId) == PrototypePlayerRole.Hunter) hunterCount++;
            Check(hunterCount == 1, "Exactly one hunter even with non-contiguous peer IDs");
        }

        var assignment = new[] { new MatchMember(0, PrototypePlayerRole.Hunter, "Demon"),
            new MatchMember(4, PrototypePlayerRole.Survivor, "UnityChan") };
        Check(RoomLaunchProtocol.TryParseMatch(RoomLaunchProtocol.StartCommand(4, assignment), out int localId, out var parsedMembers)
            && localId == 4 && parsedMembers[1].AssetName == "UnityChan", "Full assignment and registered asset name round trip");
        MatchLaunchContext.Begin(localId, parsedMembers);
        Check(MatchLaunchContext.LocalPlayerId == 4 && MatchLaunchContext.LocalRole == PrototypePlayerRole.Survivor,
            "Non-contiguous player identity survives launch");
        assignment[1].AssetName = "Demon";
        Check(MatchLaunchContext.Members[1].AssetName == "UnityChan", "Launch context owns an independent assignment copy");
        foreach (string invalid in new[] { null, "", "MARCO/3 START 9 0:Hunter:RGVtb24=", "MARCO/3 START 0 0:Survivor:RGVtb24=",
            "MARCO/3 START 0 0:Hunter:RGVtb24=|0:Survivor:RGVtb24=", "MARCO/3 START 0 0:Hunter:RGVtb24=|4:Hunter:RGVtb24=",
            "MARCO/3 START 0 0:Hunter:Li4vRGVtb24=", "MARCO/3 START 0 0:Hunter:!!", "MARCO/3 START 0 0:Ghost:RGVtb24=" })
            Check(!RoomLaunchProtocol.TryParseMatch(invalid, out _, out _), "Reject malformed, duplicate or unsafe match assignment");
        MatchLaunchContext.Clear();

        var pose = new PlayerPoseState { PlayerId = 4, Sequence = 8, X = -2.5f, Y = 1, Z = 10, Yaw = -45,
            Pitch = 85, Speed = 7.2f, Running = true, Attack = 2, Interaction = 3, InteractionSucceeded = true };
        string encodedPose = PlayerPoseProtocol.Encode(pose);
        Check(PlayerPoseProtocol.TryParse(encodedPose, out var decoded) && decoded.PlayerId == 4 && decoded.Sequence == 8
            && decoded.X == -2.5f && decoded.Running && decoded.Attack == 2 && decoded.Interaction == 3,
            "Pose, gaze, sprint and action counters round trip");
        var priorCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Check(PlayerPoseProtocol.Encode(pose) == encodedPose && PlayerPoseProtocol.TryParse(encodedPose, out _),
                "Network floats are independent of OS decimal separator");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = priorCulture; }
        foreach (string invalid in new[] { null, "", encodedPose + " extra", encodedPose.Replace("-2.5", "NaN"),
            encodedPose.Replace("-2.5", "Infinity"), encodedPose.Replace("-2.5", "10001"), encodedPose.Replace(" 85 ", " 86 "),
            "MARCO/3 POSE 4 0 0 1 0 0 0 0 0 0 0 0", "MARCO/3 POSE 4 1 0 1 0 0 0 31 0 0 0 0",
            "MARCO/3 POSE 4 1 0 1 0 0 0 0 2 0 0 0", "MARCO/3 POSE -1 1 0 1 0 0 0 0 0 0 0 0" })
            Check(!PlayerPoseProtocol.TryParse(invalid, out _), "Reject invalid, non-finite and out-of-range pose");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (var guest = new TcpClient())
            {
                var accept = listener.AcceptTcpClientAsync();
                await guest.ConnectAsync(IPAddress.Loopback, port);
                using (var host = await accept)
                using (var writer = new StreamWriter(host.GetStream()) { AutoFlush = true })
                using (var reader = new StreamReader(guest.GetStream()))
                {
                    writer.WriteLine("MARCO/2 ROSTER 0:SG9zdA==|4:R3Vlc3Q=");
                    writer.WriteLine(RoomLaunchProtocol.StartCommand(PrototypePlayerRole.Survivor));
                    var rosterTask = reader.ReadLineAsync();
                    Check(await Task.WhenAny(rosterTask, Task.Delay(3000)) == rosterTask && (await rosterTask).StartsWith("MARCO/2 ROSTER "), "Roster precedes start on actual TCP stream");
                    var startTask = reader.ReadLineAsync();
                    Check(await Task.WhenAny(startTask, Task.Delay(3000)) == startTask && RoomLaunchProtocol.TryParseStart(await startTask, out var role) && role == PrototypePlayerRole.Survivor, "Guest receives assigned role on TCP stream");
                    writer.WriteLine("MARCO/2 ROSTER 0:SG9zdA==");
                    var nextTask = reader.ReadLineAsync();
                    Check(await Task.WhenAny(nextTask, Task.Delay(3000)) == nextTask && (await nextTask).StartsWith("MARCO/2 ROSTER "), "Session remains usable after start");
                }
            }
        }
        finally { listener.Stop(); }
    }
}
