module Dynamic

open System.Threading.Tasks

let total (load: unit -> Task<int>) (xs: int list) =
    task {
        let rec sum acc =
            function
            | [] -> acc
            | x :: rest -> sum (acc + x) rest
        let! first = load ()
        return sum first xs
    }
