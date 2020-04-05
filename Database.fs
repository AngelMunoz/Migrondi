namespace Sqlator

open System.Data.SQLite
open Dapper
open FSharp.Data.Dapper
open Types

module Database =
    let inline (=>) (column: string) (value: 'T) = column, box value


    let private getConnection() = SqliteConnection(new SQLiteConnection("Data Source=Sqlator.db"))


    let find<'T> = querySeqAsync<'T> (getConnection)


    let fondn = find<Todo> { parameters (dict [ "Name" => "Peter" ]) }
