#!/bin/bash
echo "=== Testing SwapManager in Isolation ==="

echo ""
echo "1. Testing Jupiter API directly (no .NET):"
curl -s "https://quote-api.jup.ag/v6/quote?inputMint=So11111111111111111111111111111111111111112&outputMint=EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v&amountIn=1000000000&slippageBps=50" | head -c 500

echo ""
echo ""
echo "2. Testing Algorand AMM math (expected: ~996990):"
echo "Input: 1000000 microALGO, Reserve: 1T, Fee: 0.3%, Slippage: 0.5%"
echo "Formula: out = (in * 9970 * reserveOut) / (10000 * reserveIn + in * 9970)"

echo ""
echo "3. Starting .NET API on :5002..."
cd /c/Users/atooz/Programming/Projects/oasis-sleek
start /b dotnet run --project OASIS.WebAPI.csproj --urls http://localhost:5002
sleep 10

echo ""
echo "4. Testing backend Algorand quote:"
curl -s "http://localhost:5002/api/swap/quote?chain=algorand&tokenIn=0&tokenOut=31566704&amountIn=1000000&slippageBps=50" | head -c 500

echo ""
echo ""
echo "5. Testing backend Solana quote:"
curl -s "http://localhost:5002/api/swap/quote?chain=solana&tokenIn=So11111111111111111111111111111111111111112&tokenOut=EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v&amountIn=1000000000&slippageBps=50" | head -c 500
