﻿namespace Cricket

open System

module Random = 
    
    let random = System.Random()

    let randomLong() =
        let buffer = Array.zeroCreate<byte> sizeof<UInt64>
        random.NextBytes buffer
        BitConverter.ToUInt64(buffer, 0)

module Math =
    
    open System

    module Statistics =
        
        module Sampling =

            type Sample = {
                Min : int64; Max : int64; Sum : int64
                Count : int64
                M1 : float; M2 : float; M3 : float; M4 : float
            }
            with
                static member Empty = { 
                    Min = Int64.MaxValue
                    Max = Int64.MinValue
                    Sum = 0L
                    Count = 0L
                    //Central moments
                    M1 = 0.
                    M2 = 0.
                    M3 = 0.
                    M4 = 0.
                }
                member x.Mean with get() = x.M1

                member x.Variance with get() =  x.M2 / (float (x.Count - 1L))
                member x.StandardDeviation with get() = sqrt(x.Variance)
                member x.Skewness with get() = sqrt(float x.Count) * x.M3 / (Math.Pow(x.M2, 1.5))
                member x.Kurtosis with get() = ((float x.Count) * x.M4) / (x.M2 * x.M2)
                member x.ExcessKurtosis with get() = x.Kurtosis - 3.

            let empty = Sample.Empty

            //http://en.wikipedia.org/wiki/Algorithms_for_calculating_variance#Higher-order_statistics
            //An incremental algorithm for computing central moments.
            let update (x:Sample) value =
                let v = float value
                let prevCount, count = (float x.Count), x.Count + 1L
                let n = (float count)
                let delta = v - x.Mean
                let deltaN = delta / n
                let deltaN2 = deltaN * deltaN
                let term = delta * deltaN * prevCount
                { x with
                    Min = (min x.Min value)
                    Max = (max x.Max value)
                    Sum = (x.Sum + value)
                    Count = count
                    M1 = x.M1 + deltaN
                    M2 = x.M2 + term
                    M3 = x.M3 + term * deltaN * (n - 2.) - 3. * deltaN * x.M2
                    M4 = x.M4 + term * deltaN2 * (n * n - 3.* n + 3.) + 6. * deltaN2 * x.M2 - 4. * deltaN * x.M3
                }

            let compute data = data |> Seq.fold update empty

        module ExponentialWeightedAverage = 
            
            type EWMA = {
                Alpha : float
                Interval : int64 //seconds
                Count : int64
                Rate : float option
            }
            with
                member x.Value(?defaultValue) = 
                    let defValue = defaultArg defaultValue 0.
                    match x.Rate with
                    | Some(r) -> r
                    | None -> defValue

            let alpha interval w = 1. - exp(-(interval / (w * 60.)))

            let mark (x:EWMA) value = 
                { x with Count =  x.Count + value }

            let tick (x:EWMA) = 
                let rate = (float (x.Count / x.Interval))
                match x.Rate with
                | Some(r) -> 
                    { x with Rate = Some(r + (x.Alpha * (rate - r))) }
                | None -> 
                    { x with Rate = Some rate }

            let create interval averageSpan = 
                {
                   Interval = interval
                   Alpha = alpha (float interval) averageSpan
                   Count = 0L
                   Rate = None
                }
