using System.Collections.Concurrent;
using CryptoArbitrageAPI.Models;

namespace CryptoArbitrageAPI.Services
{
    public class ArbitrageService : IArbitrageService
    {
        private readonly ICoinGeckoService _coinGeckoService;
        private readonly ILogger<ArbitrageService> _logger;

        // List of stablecoins to consider
        private readonly List<string> _stableCoins = new List<string> { "usdt", "usdc", "busd", "dai" };

        public ArbitrageService(ICoinGeckoService coinGeckoService, ILogger<ArbitrageService> logger)
        {
            _coinGeckoService = coinGeckoService;
            _logger = logger;
        }

        public async Task<List<ArbitrageOpportunity>> FindArbitrageOpportunitiesAsync(string baseCoinId, int maxPathLength = 4)
        {
            // Step 1: Fetch exchange pairs
            var exchangePairs = await _coinGeckoService.GetExchangePairsAsync();


            // Step 2: Build graph from exchange pairs
            var graph = BuildGraphFromExchangePairs(exchangePairs);

            _logger.LogInformation("Graph: {}",graph);

            // Step 3: Find arbitrage opportunities starting and ending with the baseCoinId
            var opportunities = DetectArbitrage(graph, baseCoinId.ToLower(), maxPathLength);

            return opportunities;
        }

        private Dictionary<string, List<Edge>> BuildGraphFromExchangePairs(List<ExchangePair> exchangePairs)
        {
            var graph = new Dictionary<string, List<Edge>>();

            foreach (var pair in exchangePairs)
            {
                var baseCoin = pair.Base.ToLower();
                var targetCoin = pair.Target.ToLower();
                var rate = pair.Last;

                _logger.LogInformation("Rate: {rate}",rate);

                // Add edge from base to target
                if (!graph.ContainsKey(baseCoin))
                {
                    graph[baseCoin] = new List<Edge>();
                }
                graph[baseCoin].Add(new Edge
                {
                    To = targetCoin,
                    Rate = rate
                });

                // Also add the reverse edge if available
                if (!graph.ContainsKey(targetCoin))
                {
                    graph[targetCoin] = new List<Edge>();
                }
                graph[targetCoin].Add(new Edge
                {
                    To = baseCoin,
                    Rate = 1 / rate
                });
            }



            return graph;
        }

        private List<ArbitrageOpportunity> DetectArbitrage(Dictionary<string, List<Edge>> graph, string startCoin, int maxPathLength)
        {
            var opportunities = new List<ArbitrageOpportunity>();

            // Use Depth-First Search to explore possible paths up to maxPathLength
            var path = new List<string> { startCoin };
            var visited = new HashSet<string> { startCoin };

            DFS(graph, startCoin, startCoin, 1.0m, path, visited, opportunities, maxPathLength);

            return opportunities;
        }

        private void DFS(Dictionary<string, List<Edge>> graph, string currentCoin, string startCoin, decimal accumulatedRate, List<string> path, HashSet<string> visited, List<ArbitrageOpportunity> opportunities, int maxPathLength)
        {
            if (path.Count > maxPathLength)
                return;
            _logger.LogInformation("Path COunt: {}",path.Count);
            if (path.Count >= 3 && _stableCoins.Contains(currentCoin) && currentCoin == startCoin && accumulatedRate > 1.0m)
            {
                // Found an arbitrage opportunity
                var profitPercentage = (accumulatedRate - 1.0m) * 100;
                opportunities.Add(new ArbitrageOpportunity
                {
                    Path = new List<string>(path),
                    ProfitPercentage = profitPercentage
                });
                return;
            }

            if (!graph.ContainsKey(currentCoin))
                return;

            foreach (var edge in graph[currentCoin])
            {
                if (!visited.Contains(edge.To))
                {
                    visited.Add(edge.To);
                    path.Add(edge.To);

                    DFS(graph, edge.To, startCoin, accumulatedRate * edge.Rate, path, visited, opportunities, maxPathLength);

                    // Backtrack
                    visited.Remove(edge.To);
                    path.RemoveAt(path.Count - 1);
                }
                else if (edge.To == startCoin && path.Count >= 3)
                {
                    // Found a cycle back to the start coin
                    var profitPercentage = (accumulatedRate * edge.Rate - 1.0m) * 100;
                    if (profitPercentage > 0)
                    {
                        path.Add(edge.To);
                        opportunities.Add(new ArbitrageOpportunity
                        {
                            Path = new List<string>(path),
                            ProfitPercentage = profitPercentage
                        });
                        path.RemoveAt(path.Count - 1);
                    }
                }
            }
        }
    }
    public class Edge
    {
        public string To { get; set; }
        public decimal Rate { get; set; }
    }
}
