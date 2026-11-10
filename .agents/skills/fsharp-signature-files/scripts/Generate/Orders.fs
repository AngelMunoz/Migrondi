module Shop.Orders

let private taxRate = 0.2m

let roundPrice (price: decimal) = System.Math.Round(price, 2)

/// The order total with tax, rounded to cents.
let total (prices: decimal list) = prices |> List.sum |> (*) (1m + taxRate) |> roundPrice
