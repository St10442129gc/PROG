namespace SmartX.Api.Models;

/// <summary>
/// A node in a nested deployment / configuration tree, e.g.
/// Facility A -> Zone 1 -> Sub-Zone B -> Node 4.
/// </summary>
public class DeploymentNode
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Must match the node's position in <see cref="DeploymentValidator.ExpectedTierOrder"/>.</summary>
    public string Tier { get; set; } = string.Empty;

    public List<DeploymentNode> Children { get; set; } = new();
}

public record DeploymentValidationResult(bool IsValid, List<string> Errors);

// =====================================================================
// ASSIGNMENT REQUIREMENT: RECURSION
// DeploymentValidator.ValidateRecursive() below recursively walks a
// nested Facility -> Zone -> SubZone -> Node deployment tree.
// =====================================================================

/// <summary>
/// Recursively validates that a multi-tier deployment tree is well formed:
/// every branch must descend strictly through Facility -> Zone -> SubZone -> Node,
/// names must be non-empty, and no tier may be skipped or repeated out of order.
/// </summary>
public static class DeploymentValidator
{
    public static readonly string[] ExpectedTierOrder = { "Facility", "Zone", "SubZone", "Node" };

    public static DeploymentValidationResult Validate(DeploymentNode root)
    {
        var errors = new List<string>();
        ValidateRecursive(root, depth: 0, path: root.Name, errors);
        return new DeploymentValidationResult(errors.Count == 0, errors);
    }

    /// <summary>
    /// Base case: depth has walked past the deepest allowed tier or a leaf has no
    /// children left to descend into. Recursive case: validate the current node,
    /// then recurse into every child one tier deeper.
    /// </summary>
    private static void ValidateRecursive(DeploymentNode node, int depth, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(node.Name))
        {
            errors.Add($"Node at '{path}' has an empty name.");
            return; // base case: cannot validate deeper without a name
        }

        if (depth >= ExpectedTierOrder.Length)
        {
            errors.Add($"'{path}' exceeds the maximum supported nesting depth ({ExpectedTierOrder.Length} tiers).");
            return; // base case: depth exhausted
        }

        if (!string.Equals(node.Tier, ExpectedTierOrder[depth], StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"'{path}' expected tier '{ExpectedTierOrder[depth]}' at depth {depth} but found '{node.Tier}'.");
        }

        // Recursive case: descend into each child one tier deeper.
        foreach (var child in node.Children)
        {
            ValidateRecursive(child, depth + 1, $"{path} -> {child.Name}", errors);
        }
    }
}
