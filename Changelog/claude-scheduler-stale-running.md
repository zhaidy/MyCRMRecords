# Run now works again after an update stopped a task

When an update restarted MyCRMRecords while a scheduled task ran, the task showed Stopped but Run now (and its schedule)
still answered "The task is running now" for up to two hours. A stopped run no longer counts as running.
