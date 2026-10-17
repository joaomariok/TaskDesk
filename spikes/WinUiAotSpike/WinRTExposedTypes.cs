using System.Collections.ObjectModel;
using WinUiAotSpike;

// ItemsControl.ItemsSource is typed object, so the CsWinRT AOT generator cannot see that this collection
// crosses into WinRT and emits no vtable for it. Without this, Native AOT fails in ItemsSource's setter
// with "Value does not fall within the expected range", and the trimmed (JIT) build falls back to the
// reflection path and throws a NullReferenceException in WinRT.TypeExtensions.GetAbiToProjectionVftblPtr.
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<TaskRow>))]
