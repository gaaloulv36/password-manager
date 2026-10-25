using System.Diagnostics;
using System.Text;

namespace VaultForge.Core.Tests.Fuzzing;

/// <summary>
/// A small mutation fuzzer: takes valid sample inputs, corrupts them in many random ways, and checks the
/// target only fails in documented ways — no unexpected exception types, no hangs. Deterministic (seeded),
/// so a failure can be replayed. Iterations per target: FUZZ_ITERATIONS (default 1500; CI) — raise it
/// locally for a long run, e.g. FUZZ_ITERATIONS=200000.
/// </summary>
public static class Fuzzer
{
    public static int Iterations { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("FUZZ_ITERATIONS"), out var n) && n > 0 ? n : 1500;

    public static readonly TimeSpan MaxTimePerInput = TimeSpan.FromSeconds(2);

    /// <summary>Tokens that tend to break parsers: JSON/CSV structure, odd numbers, odd characters.</summary>
    private static readonly string[] Interesting =
    [
        "\"", ",", "\n", "\r\n", "{", "}", "[", "]", ":", "null", "true", "-1", "0", "-0", "1e999", "99999999999999999999",
        "2147483648", "-2147483649", "\"\"", "\\u0000", "\\", "%", "\0", "﻿", "\uD800", "ÿ", "𝔘", "otpauth://totp/x?secret=",
        "https://", "xn--", "..", "@", "=", "&", "#", new string('A', 4096), new string('[', 256), "\t",
    ];

    public static void Run(string name, IReadOnlyList<byte[]> seeds, Action<byte[]> target, params Type[] allowed)
    {
        var random = new Random(StableSeed(name)); // string.GetHashCode is randomised per process
        var timer = new Stopwatch();
        for (var i = 0; i < Iterations; i++)
        {
            var input = Mutate(seeds[random.Next(seeds.Count)], random);
            timer.Restart();
            try
            {
                target(input);
            }
            catch (Exception ex) when (allowed.Any(t => t.IsInstanceOfType(ex)))
            {
                // A documented, handled failure: fine.
            }
            catch (Exception ex)
            {
                throw new FuzzFailure(name, i, input, $"unexpected {ex.GetType().Name}: {ex.Message}", ex);
            }
            if (timer.Elapsed > MaxTimePerInput)
                throw new FuzzFailure(name, i, input, $"took {timer.Elapsed.TotalSeconds:0.0}s (limit {MaxTimePerInput.TotalSeconds}s)", null);
        }
    }

    public static void RunText(string name, IReadOnlyList<string> seeds, Action<string> target, params Type[] allowed) =>
        Run(name, seeds.Select(Encoding.UTF8.GetBytes).ToList(), bytes => target(Encoding.UTF8.GetString(bytes)), allowed);

    /// <summary>FNV-1a: the same seed for a target on every run and machine, so failures replay.</summary>
    private static int StableSeed(string name)
    {
        var hash = 2166136261u;
        foreach (var c in name) hash = (hash ^ c) * 16777619u;
        return (int)hash;
    }

    public static byte[] Mutate(byte[] seed, Random random)
    {
        var data = new List<byte>(seed);
        var count = 1 + random.Next(4);
        for (var m = 0; m < count; m++)
        {
            var at = data.Count == 0 ? 0 : random.Next(data.Count);
            switch (random.Next(8))
            {
                case 0 when data.Count > 0: // flip a bit
                    data[at] ^= (byte)(1 << random.Next(8));
                    break;
                case 1 when data.Count > 0: // random byte
                    data[at] = (byte)random.Next(256);
                    break;
                case 2: // insert random bytes
                    data.InsertRange(at, Enumerable.Range(0, 1 + random.Next(16)).Select(_ => (byte)random.Next(256)));
                    break;
                case 3 when data.Count > 0: // delete a range
                    data.RemoveRange(at, Math.Min(data.Count - at, 1 + random.Next(64)));
                    break;
                case 4 when data.Count > 0: // duplicate a range
                    var length = Math.Min(data.Count - at, 1 + random.Next(64));
                    data.InsertRange(random.Next(data.Count), data.GetRange(at, length));
                    break;
                case 5: // truncate
                    data.RemoveRange(at, data.Count - at);
                    break;
                default: // splice in a parser-breaking token
                    data.InsertRange(at, Encoding.UTF8.GetBytes(Interesting[random.Next(Interesting.Length)]));
                    break;
            }
        }
        return [.. data];
    }
}

public sealed class FuzzFailure(string target, int iteration, byte[] input, string problem, Exception? inner)
    : Exception($"Fuzz target '{target}', iteration {iteration}: {problem}\nInput (base64): {Convert.ToBase64String(input.Length > 2048 ? input[..2048] : input)}", inner);
