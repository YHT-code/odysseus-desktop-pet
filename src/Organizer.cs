using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OdysseusDesktop
{
    public sealed class TodoItem
    {
        public string Text { get; set; } = "";
        public bool Done { get; set; }
    }

    public sealed class OrganizerStore
    {
        public static OrganizerStore Instance { get; } = new OrganizerStore();
        readonly string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OdysseusDesktop");
        string TodoPath { get { return Path.Combine(dataDirectory, "todos.json"); } }
        string NotesPath { get { return Path.Combine(dataDirectory, "notes.txt"); } }

        public List<TodoItem> Todos { get; private set; } = new List<TodoItem>();
        public string NotesText { get; private set; } = "";

        OrganizerStore()
        {
            Directory.CreateDirectory(dataDirectory);
            try { if (File.Exists(TodoPath)) Todos = JsonSerializer.Deserialize<List<TodoItem>>(File.ReadAllText(TodoPath)) ?? new List<TodoItem>(); }
            catch (Exception ex) { Program.Log(ex); Todos = new List<TodoItem>(); }
            try { if (File.Exists(NotesPath)) NotesText = File.ReadAllText(NotesPath); }
            catch (Exception ex) { Program.Log(ex); }
        }

        public void AddTodo(string text)
        {
            text = (text ?? "").Trim(); if (text.Length == 0) return;
            Todos.Add(new TodoItem { Text = text }); SaveTodos();
        }
        public void RemoveTodo(TodoItem item) { Todos.Remove(item); SaveTodos(); }
        public void SaveTodos()
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(TodoPath, JsonSerializer.Serialize(Todos, new JsonSerializerOptions { WriteIndented = true }));
        }
        public void SaveNotes(string text)
        {
            NotesText = text ?? ""; Directory.CreateDirectory(dataDirectory); File.WriteAllText(NotesPath, NotesText);
        }
    }
}
