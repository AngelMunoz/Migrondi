module Static

open System.Threading.Tasks

let rec private sum acc =
    function
    | [] -> acc
    | x :: rest -> sum (acc + x) rest

let total (load: unit -> Task<int>) (xs: int list) =
    task {
        let! first = load ()
        return sum first xs
    }
