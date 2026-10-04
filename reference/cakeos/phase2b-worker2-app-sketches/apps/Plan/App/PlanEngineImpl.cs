using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace HavenOS.Apps.Plan;

public sealed class PlanEngine : IPlanEngine
{
    private readonly NativePlanEngine _native;
    private Action<PlanEngineEvent>? _eventCallback;

    public PlanEngine(string dataDirectory = "")
    {
        _native = new NativePlanEngine(dataDirectory);
        _native.SetEventCallback(OnNativeEvent);
    }

    public async Task<IReadOnlyList<PlanCalendar>> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => _native.GetCalendars(), cancellationToken);
    }

    public async Task<IReadOnlyList<PlanEvent>> GetEventsAsync(PlanTimeRange range, IReadOnlyList<string> calendarUids, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => _native.GetEvents(range, calendarUids), cancellationToken);
    }

    public async Task<IReadOnlyList<PlanTask>> GetTasksAsync(IReadOnlyList<string> calendarUids, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => _native.GetTasks(calendarUids), cancellationToken);
    }

    public async Task<PlanEvent> CreateEventAsync(PlanEvent evt, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => _native.CreateEvent(evt), cancellationToken);
    }

    public async Task<PlanEvent> UpdateEventAsync(string uid, PlanEvent evt, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => _native.UpdateEvent(uid, evt), cancellationToken);
    }

    public async Task DeleteEventAsync(string uid, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _native.DeleteEvent(uid), cancellationToken);
    }

    public async Task<PlanTask> CreateTaskAsync(PlanTask task, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => _native.CreateTask(task), cancellationToken);
    }

    public async Task<PlanTask> UpdateTaskAsync(string uid, PlanTask task, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => _native.UpdateTask(uid, task), cancellationToken);
    }

    public async Task DeleteTaskAsync(string uid, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _native.DeleteTask(uid), cancellationToken);
    }

    public async Task<PlanCalendar> CreateCalendarAsync(PlanCalendar calendar, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => _native.CreateCalendar(calendar), cancellationToken);
    }

    public async Task SetCalendarVisibilityAsync(string uid, bool visible, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _native.SetCalendarVisibility(uid, visible), cancellationToken);
    }

    public void SetEventCallback(Action<PlanEngineEvent> callback) => _eventCallback = callback;

    public ValueTask DisposeAsync()
    {
        _native.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnNativeEvent(PlanEngineEvent evt)
    {
        _eventCallback?.Invoke(evt);
    }

    private sealed class NativePlanEngine : IDisposable
    {
        private readonly IntPtr _handle;
        private Action<PlanEngineEvent>? _callback;

        public NativePlanEngine(string dataDirectory)
        {
            _handle = NativeMethods.plan_engine_create(dataDirectory ?? "");
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create PlanEngine. Ensure libical and evolution-data-server are installed.");
        }

        public IReadOnlyList<PlanCalendar> GetCalendars()
        {
            int count = NativeMethods.plan_engine_get_calendar_count(_handle);
            var calendars = new List<PlanCalendar>(count);
            for (int i = 0; i < count; i++)
            {
                var cal = new NativeCalendar();
                NativeMethods.plan_engine_get_calendar(_handle, i, ref cal);
                calendars.Add(new PlanCalendar(
                    Marshal.PtrToStringAnsi(cal.Uid) ?? "",
                    Marshal.PtrToStringAnsi(cal.Name) ?? "",
                    Marshal.PtrToStringAnsi(cal.Color) ?? "#0066CC",
                    cal.Visible != 0,
                    cal.ReadOnly != 0));
                FreeCalendar(ref cal);
            }
            return calendars;
        }

        public IReadOnlyList<PlanEvent> GetEvents(PlanTimeRange range, IReadOnlyList<string> calendarUids)
        {
            var nativeRange = new NativeTimeRange
            {
                Start = range.Start.ToUnixTimeSeconds(),
                End = range.End.ToUnixTimeSeconds()
            };
            var uidsArray = calendarUids.Count > 0 ? calendarUids.ToArray() : null;
            int count = NativeMethods.plan_engine_get_event_count(_handle, ref nativeRange, uidsArray, calendarUids.Count);
            var events = new List<PlanEvent>(count);
            for (int i = 0; i < count; i++)
            {
                var evt = new NativeEvent();
                NativeMethods.plan_engine_get_event(_handle, i, ref evt);
                events.Add(ConvertEvent(ref evt));
                FreeEvent(ref evt);
            }
            return events;
        }

        public IReadOnlyList<PlanTask> GetTasks(IReadOnlyList<string> calendarUids)
        {
            var uidsArray = calendarUids.Count > 0 ? calendarUids.ToArray() : null;
            int count = NativeMethods.plan_engine_get_task_count(_handle, uidsArray, calendarUids.Count);
            var tasks = new List<PlanTask>(count);
            for (int i = 0; i < count; i++)
            {
                var task = new NativeTask();
                NativeMethods.plan_engine_get_task(_handle, i, ref task);
                tasks.Add(ConvertTask(ref task));
                FreeTask(ref task);
            }
            return tasks;
        }

        public PlanEvent CreateEvent(PlanEvent evt)
        {
            var nativeEvt = ConvertEvent(evt);
            NativeMethods.plan_engine_create_event(_handle, ref nativeEvt);
            var result = ConvertEvent(ref nativeEvt);
            FreeEvent(ref nativeEvt);
            return result;
        }

        public PlanEvent UpdateEvent(string uid, PlanEvent evt)
        {
            var nativeEvt = ConvertEvent(evt);
            NativeMethods.plan_engine_update_event(_handle, uid, ref nativeEvt);
            var result = ConvertEvent(ref nativeEvt);
            FreeEvent(ref nativeEvt);
            return result;
        }

        public void DeleteEvent(string uid)
        {
            NativeMethods.plan_engine_delete_event(_handle, uid);
        }

        public PlanTask CreateTask(PlanTask task)
        {
            var nativeTask = ConvertTask(task);
            NativeMethods.plan_engine_create_task(_handle, ref nativeTask);
            var result = ConvertTask(ref nativeTask);
            FreeTask(ref nativeTask);
            return result;
        }

        public PlanTask UpdateTask(string uid, PlanTask task)
        {
            var nativeTask = ConvertTask(task);
            NativeMethods.plan_engine_update_task(_handle, uid, ref nativeTask);
            var result = ConvertTask(ref nativeTask);
            FreeTask(ref nativeTask);
            return result;
        }

        public void DeleteTask(string uid)
        {
            NativeMethods.plan_engine_delete_task(_handle, uid);
        }

        public PlanCalendar CreateCalendar(PlanCalendar calendar)
        {
            var nativeCal = ConvertCalendar(calendar);
            NativeMethods.plan_engine_create_calendar(_handle, ref nativeCal);
            var result = new PlanCalendar(
                Marshal.PtrToStringAnsi(nativeCal.Uid) ?? "",
                Marshal.PtrToStringAnsi(nativeCal.Name) ?? "",
                Marshal.PtrToStringAnsi(nativeCal.Color) ?? "",
                nativeCal.Visible != 0,
                nativeCal.ReadOnly != 0);
            FreeCalendar(ref nativeCal);
            return result;
        }

        public void SetCalendarVisibility(string uid, bool visible)
        {
            NativeMethods.plan_engine_set_calendar_visibility(_handle, uid, visible ? 1 : 0);
        }

        public void SetEventCallback(Action<PlanEngineEvent> callback) => _callback = callback;

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.plan_engine_destroy(_handle);
            }
        }

        private void OnNativeEvent(int type, IntPtr payloadPtr)
        {
            var payload = Marshal.PtrToStringAnsi(payloadPtr) ?? "";
            if (payloadPtr != IntPtr.Zero) NativeMethods.plan_engine_free_string(payloadPtr);
            _callback?.Invoke(new PlanEngineEvent((PlanEngineEventType)type, payload));
        }

        private static PlanEvent ConvertEvent(ref NativeEvent evt) => new PlanEvent(
            Marshal.PtrToStringAnsi(evt.Uid) ?? Guid.NewGuid().ToString(),
            Marshal.PtrToStringAnsi(evt.Summary) ?? "",
            Marshal.PtrToStringAnsi(evt.Description) ?? "",
            DateTimeOffset.FromUnixTimeSeconds(evt.Start),
            DateTimeOffset.FromUnixTimeSeconds(evt.End),
            evt.AllDay != 0,
            Marshal.PtrToStringAnsi(evt.Location) ?? "",
            evt.CategoriesCount > 0 ? GetStringArray(evt.Categories, evt.CategoriesCount) : [],
            Marshal.PtrToStringAnsi(evt.RecurrenceRule) ?? "",
            evt.AlarmsCount > 0 ? GetAlarms(evt.Alarms, evt.AlarmsCount) : []);

        private static PlanEvent ConvertEvent(PlanEvent evt)
        {
            var nativeEvt = new NativeEvent
            {
                Uid = strdup_c(evt.Uid),
                Summary = strdup_c(evt.Summary),
                Description = strdup_c(evt.Description),
                Start = evt.Start.ToUnixTimeSeconds(),
                End = evt.End.ToUnixTimeSeconds(),
                AllDay = evt.AllDay ? 1 : 0,
                Location = strdup_c(evt.Location),
                Categories = StringArrayToNative(evt.Categories),
                CategoriesCount = evt.Categories.Count,
                RecurrenceRule = strdup_c(evt.RecurrenceRule),
                Alarms = AlarmsToNative(evt.Alarms),
                AlarmsCount = evt.Alarms.Count
            };
            return nativeEvt;
        }

        private static PlanTask ConvertTask(ref NativeTask task) => new PlanTask(
            Marshal.PtrToStringAnsi(task.Uid) ?? Guid.NewGuid().ToString(),
            Marshal.PtrToStringAnsi(task.Summary) ?? "",
            Marshal.PtrToStringAnsi(task.Description) ?? "",
            task.Due > 0 ? DateTimeOffset.FromUnixTimeSeconds(task.Due) : null,
            task.Start > 0 ? DateTimeOffset.FromUnixTimeSeconds(task.Start) : null,
            task.Priority,
            task.Completed != 0,
            task.CompletedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(task.CompletedAt) : null,
            task.CategoriesCount > 0 ? GetStringArray(task.Categories, task.CategoriesCount) : []);

        private static PlanTask ConvertTask(PlanTask task)
        {
            var nativeTask = new NativeTask
            {
                Uid = strdup_c(task.Uid),
                Summary = strdup_c(task.Summary),
                Description = strdup_c(task.Description),
                Due = task.Due?.ToUnixTimeSeconds() ?? 0,
                Start = task.Start?.ToUnixTimeSeconds() ?? 0,
                Priority = task.Priority,
                Completed = task.Completed ? 1 : 0,
                CompletedAt = task.CompletedAt?.ToUnixTimeSeconds() ?? 0,
                Categories = StringArrayToNative(task.Categories),
                CategoriesCount = task.Categories.Count
            };
            return nativeTask;
        }

        private static PlanCalendar ConvertCalendar(PlanCalendar calendar)
        {
            return new NativeCalendar
            {
                Uid = strdup_c(calendar.Uid),
                Name = strdup_c(calendar.Name),
                Color = strdup_c(calendar.Color),
                Visible = calendar.Visible ? 1 : 0,
                ReadOnly = calendar.ReadOnly ? 1 : 0
            };
        }

        private static string[] GetStringArray(IntPtr array, int count)
        {
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                var ptr = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                result[i] = Marshal.PtrToStringAnsi(ptr) ?? "";
            }
            return result;
        }

        private static PlanAlarm[] GetAlarms(IntPtr array, int count)
        {
            var result = new PlanAlarm[count];
            for (int i = 0; i < count; i++)
            {
                var alarm = Marshal.PtrToStructure<NativeAlarm>(IntPtr.Add(array, i * Marshal.SizeOf<NativeAlarm>()));
                result[i] = new PlanAlarm(
                    Marshal.PtrToStringAnsi(alarm.Uid) ?? "",
                    DateTimeOffset.FromUnixTimeSeconds(alarm.Trigger),
                    Marshal.PtrToStringAnsi(alarm.Action) ?? "DISPLAY",
                    Marshal.PtrToStringAnsi(alarm.Description) ?? "");
            }
            return result;
        }

        private static IntPtr StringArrayToNative(IReadOnlyList<string> strings)
        {
            if (strings.Count == 0) return IntPtr.Zero;
            var array = Marshal.AllocHGlobal(strings.Count * IntPtr.Size);
            for (int i = 0; i < strings.Count; i++)
            {
                Marshal.WriteIntPtr(array, i * IntPtr.Size, strdup_c(strings[i]));
            }
            return array;
        }

        private static IntPtr AlarmsToNative(IReadOnlyList<PlanAlarm> alarms)
        {
            if (alarms.Count == 0) return IntPtr.Zero;
            var size = Marshal.SizeOf<NativeAlarm>();
            var array = Marshal.AllocHGlobal(alarms.Count * size);
            for (int i = 0; i < alarms.Count; i++)
            {
                var alarm = new NativeAlarm
                {
                    Uid = strdup_c(alarms[i].Uid),
                    Trigger = alarms[i].Trigger.ToUnixTimeSeconds(),
                    Action = strdup_c(alarms[i].Action),
                    Description = strdup_c(alarms[i].Description)
                };
                Marshal.StructureToPtr(alarm, IntPtr.Add(array, i * size), false);
            }
            return array;
        }

        private static void FreeCalendar(ref NativeCalendar cal)
        {
            if (cal.Uid != IntPtr.Zero) { NativeMethods.plan_engine_free_string(cal.Uid); cal.Uid = IntPtr.Zero; }
            if (cal.Name != IntPtr.Zero) { NativeMethods.plan_engine_free_string(cal.Name); cal.Name = IntPtr.Zero; }
            if (cal.Color != IntPtr.Zero) { NativeMethods.plan_engine_free_string(cal.Color); cal.Color = IntPtr.Zero; }
        }

        private static void FreeEvent(ref NativeEvent evt)
        {
            if (evt.Uid != IntPtr.Zero) { NativeMethods.plan_engine_free_string(evt.Uid); evt.Uid = IntPtr.Zero; }
            if (evt.Summary != IntPtr.Zero) { NativeMethods.plan_engine_free_string(evt.Summary); evt.Summary = IntPtr.Zero; }
            if (evt.Description != IntPtr.Zero) { NativeMethods.plan_engine_free_string(evt.Description); evt.Description = IntPtr.Zero; }
            if (evt.Location != IntPtr.Zero) { NativeMethods.plan_engine_free_string(evt.Location); evt.Location = IntPtr.Zero; }
            if (evt.RecurrenceRule != IntPtr.Zero) { NativeMethods.plan_engine_free_string(evt.RecurrenceRule); evt.RecurrenceRule = IntPtr.Zero; }
            if (evt.Categories != IntPtr.Zero) { FreeStringArray(evt.Categories, evt.CategoriesCount); evt.Categories = IntPtr.Zero; }
            if (evt.Alarms != IntPtr.Zero) { FreeAlarms(evt.Alarms, evt.AlarmsCount); evt.Alarms = IntPtr.Zero; }
        }

        private static void FreeTask(ref NativeTask task)
        {
            if (task.Uid != IntPtr.Zero) { NativeMethods.plan_engine_free_string(task.Uid); task.Uid = IntPtr.Zero; }
            if (task.Summary != IntPtr.Zero) { NativeMethods.plan_engine_free_string(task.Summary); task.Summary = IntPtr.Zero; }
            if (task.Description != IntPtr.Zero) { NativeMethods.plan_engine_free_string(task.Description); task.Description = IntPtr.Zero; }
            if (task.Categories != IntPtr.Zero) { FreeStringArray(task.Categories, task.CategoriesCount); task.Categories = IntPtr.Zero; }
        }

        private static void FreeStringArray(IntPtr array, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var ptr = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                if (ptr != IntPtr.Zero) NativeMethods.plan_engine_free_string(ptr);
            }
            Marshal.FreeHGlobal(array);
        }

        private static void FreeAlarms(IntPtr array, int count)
        {
            var size = Marshal.SizeOf<NativeAlarm>();
            for (int i = 0; i < count; i++)
            {
                var alarmPtr = IntPtr.Add(array, i * size);
                var alarm = Marshal.PtrToStructure<NativeAlarm>(alarmPtr);
                if (alarm.Uid != IntPtr.Zero) NativeMethods.plan_engine_free_string(alarm.Uid);
                if (alarm.Action != IntPtr.Zero) NativeMethods.plan_engine_free_string(alarm.Action);
                if (alarm.Description != IntPtr.Zero) NativeMethods.plan_engine_free_string(alarm.Description);
            }
            Marshal.FreeHGlobal(array);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeCalendar
        {
            public IntPtr Uid;
            public IntPtr Name;
            public IntPtr Color;
            public int Visible;
            public int ReadOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeEvent
        {
            public IntPtr Uid;
            public IntPtr Summary;
            public IntPtr Description;
            public long Start;
            public long End;
            public int AllDay;
            public IntPtr Location;
            public IntPtr Categories;
            public int CategoriesCount;
            public IntPtr RecurrenceRule;
            public IntPtr Alarms;
            public int AlarmsCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeTask
        {
            public IntPtr Uid;
            public IntPtr Summary;
            public IntPtr Description;
            public long Due;
            public long Start;
            public int Priority;
            public int Completed;
            public long CompletedAt;
            public IntPtr Categories;
            public int CategoriesCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeAlarm
        {
            public IntPtr Uid;
            public long Trigger;
            public IntPtr Action;
            public IntPtr Description;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeTimeRange
        {
            public long Start;
            public long End;
        }

        private static class NativeMethods
        {
            private const string LibraryName = "cakeos-plan-engine";

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr plan_engine_create(string dataDirectory);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_destroy(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int plan_engine_get_calendar_count(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_get_calendar(IntPtr handle, int index, ref NativeCalendar calendar);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int plan_engine_get_event_count(IntPtr handle, ref NativeTimeRange range, string[] calendarUids, int calendarUidsCount);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_get_event(IntPtr handle, int index, ref NativeEvent evt);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int plan_engine_get_task_count(IntPtr handle, string[] calendarUids, int calendarUidsCount);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_get_task(IntPtr handle, int index, ref NativeTask task);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_create_event(IntPtr handle, ref NativeEvent evt);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_update_event(IntPtr handle, string uid, ref NativeEvent evt);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_delete_event(IntPtr handle, string uid);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_create_task(IntPtr handle, ref NativeTask task);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_update_task(IntPtr handle, string uid, ref NativeTask task);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_delete_task(IntPtr handle, string uid);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_create_calendar(IntPtr handle, ref NativeCalendar calendar);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_set_calendar_visibility(IntPtr handle, string uid, int visible);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_set_event_callback(IntPtr handle, IntPtr callback);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void plan_engine_free_string(IntPtr ptr);
        }

        private static char* strdup_c(string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0);
            return (char*)ptr;
        }
    }
}