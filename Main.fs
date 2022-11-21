open System
open System.Runtime.InteropServices
open System.Threading.Tasks
open Microsoft.FSharp.NativeInterop
open Spectre.Console

module LibSodium =
    //
    // Update the DllImport Path to match your libsodium path (no path means "next to" Main.fs)
    //                 |
    //                 v
    [<DllImport("libsodium.dll", CharSet = CharSet.Unicode)>]
    extern int crypto_sign_keypair(byte[] publicKey, byte[] privateKey)
    // https://libsodium.gitbook.io/doc/public-key_cryptography/public-key_signatures#key-pair-generations

module ConsoleHelpers =
    let markupln s = AnsiConsole.MarkupLine s
    let figletln s = 
        let title = new FigletText(s)
        title.Alignment <- Justify.Left
        title.Color <- Color.Orange1
        AnsiConsole.Write title
    let ruleln s =
        let r = new Rule(s)
        r.Alignment <- Justify.Left
        r.Border <- BoxBorder.Double
        AnsiConsole.Write r

open ConsoleHelpers

[<EntryPoint>]
let main _ =
    let CPU_COUNT = Environment.ProcessorCount

    let watch = new System.Diagnostics.Stopwatch()
    let runtime (ts: TimeSpan) (keyTries) = 
        markupln (sprintf "Runtime: %dd+%Ah:%Am:%As.%A" ts.Days ts.Hours ts.Minutes ts.Seconds ts.Milliseconds)

    Console.CancelKeyPress.Add(
        fun _ -> 
            watch.Stop()
            runtime watch.Elapsed 0
        )

    figletln "Algomania"
    ruleln "Generate an Algorand address by prefix, eg. [underline orange1]VANITY[/]L47ZPC6JCIIHGMPCGAKONR2R7XZHYIIFBHN2QAN22ZP5HQ"
    ruleln "https://github.com/kirse/algomania"
    ruleln (sprintf "Detected CPU Count: %A" CPU_COUNT)
    AnsiConsole.WriteLine()

    let tp = TextPrompt<string>("Enter a vanity prefix (valid: A-Z, 2-7):")
    tp.ValidationErrorMessage <- "Requirements -> Length > 1 char, [underline red]no[/] 0, 1, 8"
    tp.Validate(fun input -> not (input.Length = 1 || input.Contains("0") || input.Contains("1") || input.Contains("8"))) |> ignore
    let tp2 = TextPrompt<int>("How many tries per CPU? (0 for infinite):")

    let vanity = AnsiConsole.Prompt(tp).Trim().ToUpper()
    let base32 = SimpleBase.Base32.Rfc4648.Decode(vanity)
    AnsiConsole.WriteLine (sprintf "Pub-Key byte prefix is: %A" base32)

    match base32.Length with
    | 5 -> markupln "[red]WARNING:[/] 5-byte prefixes may take many runs + weeks on exceptional hardware."
    | 6 | 7 | 8 -> markupln "[red]WARNING:[/] Byte prefix of length 6-8 is uncharted space, hope you're feeling lucky."
    | l when l = 0 || l >= 9 -> failwith "Unsupported byte prefix length."
    | _ -> ()

    let tries : uint64 = (pown ((uint64)256) base32.Length) * (if base32.Length % 5 = 0 then 1UL else 32UL)
    markupln (sprintf "Approx. 1 vanity address for every ~%s tries" (tries.ToString("N0")))
    let MAX_TRIES = AnsiConsole.Prompt(tp2)
    let total : int64 = (int64)CPU_COUNT * (int64)MAX_TRIES
    markupln (sprintf "Keygen configured: %s tries" (if total = 0 then "Infinity" else total.ToString("N0")))
    markupln "Keygen running, logging to console and [underline orange1]vanity.txt[/]... <Ctrl+C to stop>"
    markupln ""

    let keyMatcher (b32 : byte array) = 
        let inline m1 (pk : byte array) =          pk[0] = b32[0]
        let inline m2 (pk : byte array) = m1 pk && pk[1] = b32[1]
        let inline m3 (pk : byte array) = m2 pk && pk[2] = b32[2]
        let inline m4 (pk : byte array) = m3 pk && pk[3] = b32[3]
        let inline m5 (pk : byte array) = m4 pk && pk[4] = b32[4]
        let inline m6 (pk : byte array) = m5 pk && pk[5] = b32[5]
        let inline m7 (pk : byte array) = m6 pk && pk[6] = b32[6]
        let inline m8 (pk : byte array) = m7 pk && pk[7] = b32[7]
        match b32.Length with
        | 1 -> m1 | 2 -> m2 | 3 -> m3 | 4 -> m4 | 5 -> m5 | 6 -> m6 | 7 -> m7 | 8 -> m8
        | _ -> failwith "Unsupported byte prefix length."
    let matchFn = keyMatcher base32

    let taskMan numTasks maxTries =
        let publicKey = Array.zeroCreate numTasks
        let privateKey = Array.zeroCreate numTasks
        let tasks = Array.zeroCreate numTasks
        for i in 0 .. (numTasks - 1) do
            publicKey[i] <- Array.zeroCreate 32
            privateKey[i] <- Array.zeroCreate 64
            tasks[i] <- Task.Factory.StartNew(fun () -> 
                try
                    let generate () =
                        LibSodium.crypto_sign_keypair(publicKey[i], privateKey[i]) |> ignore
                        if matchFn publicKey[i] then
                            let acct = Algorand.Account.AccountFromPrivateKey(privateKey[i][0..31])
                            if (acct.Address.ToString().StartsWith(vanity)) then
                                markupln (sprintf "> CPU %A -> %A %A" i (acct.Address) (acct.ToMnemonic()))
                                System.IO.File.AppendAllText("vanity.txt", sprintf "%A \n %A \n" (acct.Address) (acct.ToMnemonic()))
                    if maxTries = 0 then while true do generate()
                    else for n = 1 to maxTries do generate()
                with ex -> markupln (sprintf "%A" ex)
            )
        Task.WaitAll(tasks)
    watch.Start()
    taskMan CPU_COUNT MAX_TRIES
    watch.Stop()
    runtime watch.Elapsed MAX_TRIES
    0
