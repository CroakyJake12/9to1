using Haven.Core.Games;
using Xunit;

namespace Haven.Core.Tests;

public sealed class GamesSceneHierarchyTests
{
    private static GamesVector3 V(float x, float y, float z) => new(x, y, z);
    private static GamesSpatialComponent S(Guid id, float x = 0, float y = 0, float z = 0) => new(id, V(x, y, z));

    [Fact]
    public void DuplicateSubtree_CreatesFreshIdentities_PreservesMeshAndOriginal()
    {
        var rootID = Guid.NewGuid();
        var childID = Guid.NewGuid();
        var grandChildID = Guid.NewGuid();
        var meshID = Guid.NewGuid();

        var root = new GamesSceneNode(rootID, Guid.NewGuid(), null, "Root", S(Guid.NewGuid()));
        var child = new GamesSceneNode(childID, Guid.NewGuid(), rootID, "Child", S(Guid.NewGuid()), meshID);
        var grandChild = new GamesSceneNode(grandChildID, Guid.NewGuid(), childID, "GrandChild", S(Guid.NewGuid()));

        var mesh = new GamesMeshResource(meshID, V(1, 1, 1));
        var scene = new GamesSceneSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, [root, child, grandChild], [mesh]).Capture();
        var originalNodes = scene.Nodes.Count;
        var originalChildMesh = child.MeshResourceID;

        // Duplicate the child subtree (child + grandchild)
        var edited = GamesSceneEdits.DuplicateSubtree(scene, 1, childID);

        Assert.Equal(2, edited.Revision);
        Assert.Equal(originalNodes + 2, edited.Nodes.Count);

        // Verify original is unchanged
        Assert.Equal(originalNodes, scene.Nodes.Count);
        Assert.Equal(1, scene.Revision);

        // Find the new duplicate of 'Child' (it should have same parent as original child: rootID)
        var newChild = edited.Nodes.Single(n => n.ParentNodeID == rootID && n.Name == "Child" && n.NodeID != childID);
        Assert.NotEqual(childID, newChild.NodeID);
        Assert.NotEqual(child.EntityID, newChild.EntityID);
        Assert.NotEqual(child.Spatial.ComponentID, newChild.Spatial.ComponentID);

        // Verify MeshResourceID is preserved (shared asset identity)
        Assert.Equal(originalChildMesh, newChild.MeshResourceID);

        // Find the new duplicate of 'GrandChild' (should be child of newChild)
        var newGrandChild = edited.Nodes.Single(n => n.ParentNodeID == newChild.NodeID && n.Name == "GrandChild");
        Assert.NotEqual(grandChildID, newGrandChild.NodeID);
        Assert.NotEqual(grandChild.EntityID, newGrandChild.EntityID);
    }

    [Fact]
    public void ReparentNode_RejectsNullDestinationForNonRoot()
    {
        var rootID = Guid.NewGuid();
        var childID = Guid.NewGuid();
        var root = new GamesSceneNode(rootID, Guid.NewGuid(), null, "Root", S(Guid.NewGuid()));
        var child = new GamesSceneNode(childID, Guid.NewGuid(), rootID, "Child", S(Guid.NewGuid()));
        var scene = new GamesSceneSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, [root, child], []).Capture();

        Assert.Throws<InvalidOperationException>(() => GamesSceneEdits.ReparentNode(scene, 1, childID, null));
    }

    [Fact]
    public void DuplicateSubtree_RejectsRoot()
    {
        var rootID = Guid.NewGuid();
        var root = new GamesSceneNode(rootID, Guid.NewGuid(), null, "Root", S(Guid.NewGuid()));
        var scene = new GamesSceneSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, [root], []).Capture();

        Assert.Throws<InvalidOperationException>(() => GamesSceneEdits.DuplicateSubtree(scene, 1, rootID));
    }

    [Fact]
    public void DuplicateSubtree_RejectsStaleRevision()
    {
        var rootID = Guid.NewGuid();
        var childID = Guid.NewGuid();
        var root = new GamesSceneNode(rootID, Guid.NewGuid(), null, "Root", S(Guid.NewGuid()));
        var child = new GamesSceneNode(childID, Guid.NewGuid(), rootID, "Child", S(Guid.NewGuid()));
        var scene = new GamesSceneSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, [root, child], []).Capture();

        Assert.Throws<InvalidOperationException>(() => GamesSceneEdits.DuplicateSubtree(scene, 99, childID));
    }

    [Fact]
    public void ReparentNode_ValidMove_UpdatesParent()
    {
        var rootID = Guid.NewGuid();
        var aID = Guid.NewGuid();
        var bID = Guid.NewGuid();

        var root = new GamesSceneNode(rootID, Guid.NewGuid(), null, "Root", S(Guid.NewGuid()));
        var a = new GamesSceneNode(aID, Guid.NewGuid(), rootID, "A", S(Guid.NewGuid()));
        var b = new GamesSceneNode(bID, Guid.NewGuid(), rootID, "B", S(Guid.NewGuid()));

        var scene = new GamesSceneSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, [root, a, b], []).Capture();

        // Move A under B
        var edited = GamesSceneEdits.ReparentNode(scene, 1, aID, bID);

        Assert.Equal(2, edited.Revision);
        var movedA = edited.Nodes.Single(n => n.NodeID == aID);
        Assert.Equal(bID, movedA.ParentNodeID);
    }

    [Fact]
    public void ReparentNode_RejectsCyclic()
    {
        var rootID = Guid.NewGuid();
        var aID = Guid.NewGuid();
        var bID = Guid.NewGuid();

        var root = new GamesSceneNode(rootID, Guid.NewGuid(), null, "Root", S(Guid.NewGuid()));
        var a = new GamesSceneNode(aID, Guid.NewGuid(), rootID, "A", S(Guid.NewGuid()));
        var b = new GamesSceneNode(bID, Guid.NewGuid(), aID, "B", S(Guid.NewGuid()));

        var scene = new GamesSceneSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, [root, a, b], []).Capture();

        // Try to move A under B (creates cycle A->B->A)
        Assert.Throws<InvalidOperationException>(() => GamesSceneEdits.ReparentNode(scene, 1, aID, bID));
    }

    [Fact]
    public void ReparentNode_RejectsRoot()
    {
        var rootID = Guid.NewGuid();
        var root = new GamesSceneNode(rootID, Guid.NewGuid(), null, "Root", S(Guid.NewGuid()));
        var scene = new GamesSceneSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, [root], []).Capture();

        Assert.Throws<InvalidOperationException>(() => GamesSceneEdits.ReparentNode(scene, 1, rootID, null));
    }
}
