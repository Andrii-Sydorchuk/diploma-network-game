public interface IInteractable
{
    // Текст підказки на екрані (наприклад: "Натисніть [E] щоб відкрити")
    string GetPromptText();

    // Сама дія при натисканні клавіші
    void Interact();
}