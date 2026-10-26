using Haven.UI.Components;

namespace Haven.Desktop.Prefabs;

public sealed class ChatAddMenuPrefab : HavenPrefabDefinition
{
    public override string PrefabID => "ChatAddMenu";

    public override void OnCreated(Prefab instance)
    {
        var dismiss = instance.GetComponent<Button>("Dismiss");
        dismiss.Accessibility.AccessibleName = "Close Add menu";
        dismiss.Accessibility.Description = "Close the Add menu.";
        instance.GetComponent<Button>("CatalogBack").Accessibility.AccessibleName = "Back to Add menu";
        instance.GetComponent<Button>("Agents").Accessibility.AccessibleName = "Specialists";
        instance.GetComponent<Button>("Assistants").Accessibility.AccessibleName = "Assistants";
        instance.GetComponent<Button>("Assistants").Accessibility.Description = "Browse configured Assistants or open the Assistants workspace to create one.";
        instance.GetComponent<Button>("Instructions").Accessibility.AccessibleName = "Skills";
        instance.GetComponent<Button>("Capabilities").Accessibility.AccessibleName = "Plugin";
        instance.GetComponent<Button>("Apps").Accessibility.AccessibleName = "Apps";
        instance.GetComponent<Button>("AllowActions").Accessibility.AccessibleName = "Allow Actions";
        instance.GetComponent<Button>("VisualResponses").Accessibility.AccessibleName = "Prefer Visual Responses";
        instance.GetComponent<Button>("AttachFiles").Accessibility.AccessibleName = "Attach File(s)";
        instance.GetComponent<Input>("CatalogSearch").Accessibility.AccessibleName = "Search Add catalogue";
    }
}
