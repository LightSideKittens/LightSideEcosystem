#include <mach/mach.h>
#include <objc/message.h>
#include <objc/runtime.h>
#include <sys/resource.h>
#include <unistd.h>

int proc_pid_rusage(int pid, int flavor, rusage_info_t *buffer);
int proc_reset_footprint_interval(pid_t pid);

long long LightSideBenchmarkPhysFootprint(void)
{
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    if (task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count) != KERN_SUCCESS)
        return -1;
    return (long long)info.phys_footprint;
}

long long LightSideBenchmarkPhysFootprintIntervalMax(void)
{
    struct rusage_info_v4 info;
    if (proc_pid_rusage(getpid(), RUSAGE_INFO_V4, (rusage_info_t *)&info) != 0)
        return -1;
    return (long long)info.ri_interval_max_phys_footprint;
}

int LightSideBenchmarkResetPhysFootprintInterval(void)
{
    return proc_reset_footprint_interval(getpid());
}

long LightSideBenchmarkThermalState(void)
{
    Class processInfoClass = objc_getClass("NSProcessInfo");
    if (processInfoClass == NULL)
        return -1;
    id processInfo = ((id (*)(Class, SEL))objc_msgSend)(processInfoClass, sel_registerName("processInfo"));
    if (processInfo == NULL)
        return -1;
    return ((long (*)(id, SEL))objc_msgSend)(processInfo, sel_registerName("thermalState"));
}
