namespace Sample

/// An email address that contains one '@'.
type Email

[<RequireQualifiedAccess>]
module Email =
    /// Parses an email address. Leading and trailing spaces are removed.
    val create: input: string -> Result<Email, string>

    val value: email: Email -> string

    val internal isValid: input: string -> bool

[<RequireQualifiedAccess>]
type Status =
    | Active
    | Closed

/// A ledger that releases its handle when disposed.
type Ledger =
    interface System.IDisposable
    new: unit -> Ledger
    static member (+): ledger: Ledger * entries: int -> Ledger
