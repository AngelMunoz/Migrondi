namespace Sample

type Email = { Value: string }

[<RequireQualifiedAccess>]
module Email =
    let normalize (input: string) = input.Trim()

    let isValid (input: string) = input.Split('@').Length = 2

    /// Implementation text that callers never see.
    let create (input: string) =
        let text = normalize input
        if isValid text then Ok { Value = text } else Error $"'{input}' is not an email address."

    /// The text of the email address.
    let value (email: Email) = email.Value

[<RequireQualifiedAccess>]
type Status =
    | Active
    | Closed

type Ledger() =
    static member (+)(ledger: Ledger, _entries: int) = ledger

    interface System.IDisposable with
        member _.Dispose() = ()
