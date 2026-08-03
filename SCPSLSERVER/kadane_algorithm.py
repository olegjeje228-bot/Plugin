# Kadane's Algorithm - Maximum Subarray Sum
# Finds the contiguous subarray with the largest sum in O(n) time

def max_subarray_sum(arr):
    """
    Find the maximum sum of any contiguous subarray.
    
    Args:
        arr: List of integers
    
    Returns:
        tuple: (maximum sum, start index, end index)
    """
    if not arr:
        return 0, -1, -1
    
    max_ending_here = arr[0]
    max_so_far = arr[0]
    start = 0
    end = 0
    temp_start = 0
    
    for i in range(1, len(arr)):
        # If adding current element is better than starting fresh
        if arr[i] > max_ending_here + arr[i]:
            max_ending_here = arr[i]
            temp_start = i
        else:
            max_ending_here = max_ending_here + arr[i]
        
        # Update global maximum if needed
        if max_ending_here > max_so_far:
            max_so_far = max_ending_here
            start = temp_start
            end = i
    
    return max_so_far, start, end


def print_subarray(arr, start, end):
    """Print the subarray with its indices."""
    if start == -1:
        print("Array is empty")
        return
    
    subarray = arr[start:end + 1]
    print(f"Subarray: {subarray}")
    print(f"Indices: {start} to {end}")
    print(f"Length: {len(subarray)}")


# Example usage
if __name__ == "__main__":
    # Test cases
    test_cases = [
        [-2, 1, -3, 4, -1, 2, 1, -5, 4],  # Classic example
        [1, 2, 3, 4, 5],                    # All positive
        [-1, -2, -3, -4, -5],              # All negative
        [5, -2, 3, -1, 4, -6, 2],          # Mixed
        [0, 0, 0, 0],                      # All zeros
        [-2, -3, 4, -1, -2, 1, 5, -3],    # Another example
    ]
    
    print("=== Kadane's Algorithm - Maximum Subarray Sum ===\n")
    
    for i, arr in enumerate(test_cases, 1):
        print(f"Test Case {i}: {arr}")
        max_sum, start, end = max_subarray_sum(arr)
        print(f"Maximum sum: {max_sum}")
        print_subarray(arr, start, end)
        print("-" * 40)

    # Real-world example: Stock market profits
    print("\n=== Real-world Example: Stock Market ===")
    daily_profits = [3, -2, 5, -1, 2, -4, 3, 1, -3, 2]
    max_profit, buy_day, sell_day = max_subarray_sum(daily_profits)
    
    print(f"Daily price changes: {daily_profits}")
    print(f"Maximum profit: {max_profit}")
    print(f"Buy on day {buy_day}, sell on day {sell_day}")
    print_subarray(daily_profits, buy_day, sell_day)
