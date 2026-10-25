/* Original Linux Root controlled child. No setuid installation, shell or PATH.
 * This executable must belong to the publisher-verified protected Root cohort.
 * stdin/stdout are one private supervisor control channel, never child stdio.
 * The single-threaded native supervisor alone forks, maps a Root-owned user
 * namespace, permanently drops credentials and fd-execs the exact held ELF.
 */
#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <grp.h>
#include <linux/openat2.h>
#include <poll.h>
#include <pwd.h>
#include <sched.h>
#include <signal.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <sys/resource.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <sys/types.h>
#include <sys/sysmacros.h>
#include <sys/wait.h>
#include <unistd.h>

static pid_t owned_child=-1;
static int child_exec_error=-1;
static void fail(const char *message) {
    int error=errno?errno:EINVAL;
    if(child_exec_error>=0) {
        const char *cursor=(const char *)&error; size_t remaining=sizeof(error);
        while(remaining) {
            ssize_t count=write(child_exec_error,cursor,remaining);
            if(count<0 && errno==EINTR) continue;
            if(count<=0) break;
            cursor+=count; remaining-=(size_t)count;
        }
    }
    if(owned_child>0) kill(owned_child,SIGKILL);
    dprintf(STDERR_FILENO,"RootControlledLaunchRefused:%s:%d\n",message,error);
    _exit(70);
}
static unsigned long long number(const char *text) {
    if (!text || !*text || *text=='-' || *text=='+') fail("number");
    char *end=NULL; errno=0; unsigned long long value=strtoull(text,&end,10);
    if (errno || !end || *end) fail("number");
    return value;
}
static void write_exact(int fd,const void *bytes,size_t count) {
    const char *cursor=bytes;
    while(count) { ssize_t written=write(fd,cursor,count); if(written<0 && errno==EINTR) continue; if(written<=0) fail("write"); cursor+=written; count-=(size_t)written; }
}
static void expect_byte(int fd,char expected) {
    struct pollfd poller={.fd=fd,.events=POLLIN};
    int ready; do { ready=poll(&poller,1,15000); } while(ready<0 && errno==EINTR);
    char actual=0; if(ready!=1 || read(fd,&actual,1)!=1 || actual!=expected) fail("bounded-control-frame");
}
static void proc_map(pid_t child,const char *leaf,const char *mapping) {
    char path[96]; int length=snprintf(path,sizeof(path),"/proc/%ld/%s",(long)child,leaf);
    if(length<=0 || (size_t)length>=sizeof(path)) fail("map-path");
    int fd=open(path,O_WRONLY|O_CLOEXEC|O_NOFOLLOW); if(fd<0) fail("open-child-map");
    write_exact(fd,mapping,strlen(mapping)); if(close(fd)) fail("close-child-map");
}
static int beneath(int parent,const char *relative,int flags) {
    struct open_how how={.flags=(uint64_t)flags,.resolve=RESOLVE_BENEATH|RESOLVE_NO_SYMLINKS|RESOLVE_NO_MAGICLINKS|RESOLVE_NO_XDEV};
    int fd=(int)syscall(SYS_openat2,parent,relative,&how,sizeof(how)); if(fd<0) fail("openat2-required"); return fd;
}
static void private_stdio(void) {
    int null=open("/dev/null",O_RDWR|O_CLOEXEC); if(null<0) fail("null");
    for(int fd=0;fd<=2;fd++) if(dup2(null,fd)<0) fail("child-stdio");
    if(null>2) close(null);
}
static int safe_relative(const char *value) {
    if(!value || !*value || *value=='/' || strlen(value)>1024 || strchr(value,'\\') || strchr(value,':')) return 0;
    const char *cursor=value;
    while(*cursor) { const char *end=strchr(cursor,'/'); size_t length=end?(size_t)(end-cursor):strlen(cursor);
        if(!length || (length==1 && cursor[0]=='.') || (length==2 && cursor[0]=='.' && cursor[1]=='.')) return 0;
        if(!end) return 1;
        cursor=end+1; }
    return 0;
}
int main(int argc,char **argv) {
    /* Fixed positional internal protocol; no public Root API forwards user argv. */
    if(argc!=16 || (strcmp(argv[1],"--original-home-v1") && strcmp(argv[1],"--original-selected-v1"))) { errno=EINVAL; fail("arguments"); }
    if(getuid()!=0 || geteuid()!=0 || getgid()!=0 || getegid()!=0) { errno=EPERM; fail("actual-root-required"); }
    unsigned long long chosen=number(argv[2]); if(!chosen || chosen>=UINT32_MAX) fail("target-uid");
    uid_t uid=(uid_t)chosen;
    const char *root=argv[3],*entry=argv[4],*endpoint=argv[13],*session_type=argv[14],*display=argv[15];
    unsigned long long inode=number(argv[5]),device_major=number(argv[6]),device_minor=number(argv[7]),size=number(argv[8]);
    unsigned long long modified_seconds=number(argv[9]),modified_nanos=number(argv[10]),changed_seconds=number(argv[11]),changed_nanos=number(argv[12]);
    if(root[0]!='/' || endpoint[0]!='/' || strlen(root)>4096 || strlen(endpoint)>100 || !safe_relative(entry) ||
       (strcmp(session_type,"wayland") && strcmp(session_type,"x11"))) fail("bounded-original-layout");
    if(!strcmp(session_type,"x11")) {
        if(display[0]!=':' || strlen(display)<2 || strlen(display)>128) fail("logind-display");
        for(const char *cursor=display+1;*cursor;cursor++) if((*cursor<'0'||*cursor>'9') && *cursor!='.') fail("logind-display");
    }
    struct passwd account,*found=NULL; char account_buffer[65536];
    if(getpwuid_r(uid,&account,account_buffer,sizeof(account_buffer),&found) || !found || !account.pw_name ||
       !account.pw_dir || account.pw_dir[0]!='/') fail("actual-os-account");
    gid_t groups[128]; int group_count=128;
    if(getgrouplist(account.pw_name,account.pw_gid,groups,&group_count)<0 || group_count<1 || group_count>128) fail("bounded-os-groups");
    int volume=open("/",O_RDONLY|O_DIRECTORY|O_CLOEXEC); if(volume<0) fail("volume");
    /* Cross-mount root lookup is done one protected component at a time by managed
     * admission; this final no-symlink absolute lookup binds its held directory. */
    struct open_how root_how={.flags=O_RDONLY|O_DIRECTORY|O_CLOEXEC,.resolve=RESOLVE_NO_SYMLINKS|RESOLVE_NO_MAGICLINKS};
    int directory=(int)syscall(SYS_openat2,volume,root+1,&root_how,sizeof(root_how)); close(volume);
    if(directory<0) fail("protected-code-root");
    struct stat directory_stat; if(fstat(directory,&directory_stat) || !S_ISDIR(directory_stat.st_mode) || directory_stat.st_uid ||
        (directory_stat.st_mode&07777)!=0755) fail("protected-code-root-mode");
    int image=beneath(directory,entry,O_RDONLY|O_CLOEXEC|O_NONBLOCK);
    struct stat before; if(fstat(image,&before) || !S_ISREG(before.st_mode) || before.st_nlink!=1 || before.st_uid ||
        (before.st_mode&07777)!=0755 || (unsigned long long)before.st_ino!=inode || (unsigned long long)major(before.st_dev)!=device_major ||
        (unsigned long long)minor(before.st_dev)!=device_minor || (unsigned long long)before.st_size!=size ||
        (unsigned long long)before.st_mtim.tv_sec!=modified_seconds || (unsigned long long)before.st_mtim.tv_nsec!=modified_nanos ||
        (unsigned long long)before.st_ctim.tv_sec!=changed_seconds || (unsigned long long)before.st_ctim.tv_nsec!=changed_nanos) fail("held-original-image");
    unsigned char header[20]; if(pread(image,header,sizeof(header),0)!=(ssize_t)sizeof(header) || memcmp(header,"\177ELF",4) || header[4]!=2 || header[5]!=1) fail("native-elf-only");
    int ready[2],release[2],executed[2];
    if(pipe2(ready,O_CLOEXEC) || pipe2(release,O_CLOEXEC) || pipe2(executed,O_CLOEXEC)) fail("original-pipes");
    pid_t child=fork(); if(child<0) fail("native-fork");
    if(!child) {
        child_exec_error=executed[1];
        pid_t parent=getppid(); close(ready[0]); close(release[1]); close(executed[0]);
        /* Namespace ownership is Root, before dropping credentials. Ordinary host
         * same-UID processes lack ptrace capability in this distinct credential
         * namespace, including after exec resets dumpability. No weaker fallback. */
        if(unshare(CLONE_NEWUSER)) fail("controlled-user-namespace-required");
        write_exact(ready[1],"N",1); expect_byte(release[0],'M');
        if(setgroups((size_t)group_count,groups) || setresgid(account.pw_gid,account.pw_gid,account.pw_gid) || setresuid(uid,uid,uid)) fail("permanent-credential-drop");
        uid_t real_uid,effective_uid,saved_uid; gid_t real_gid,effective_gid,saved_gid;
        if(getresuid(&real_uid,&effective_uid,&saved_uid) || real_uid!=uid || effective_uid!=uid || saved_uid!=uid ||
           getresgid(&real_gid,&effective_gid,&saved_gid) || real_gid!=account.pw_gid || effective_gid!=account.pw_gid || saved_gid!=account.pw_gid ||
           prctl(PR_SET_NO_NEW_PRIVS,1,0,0,0) || prctl(PR_SET_DUMPABLE,0,0,0,0) || prctl(PR_SET_PDEATHSIG,SIGKILL,0,0,0) || getppid()!=parent) fail("restricted-child-state");
        struct rlimit core={0,0}; if(setrlimit(RLIMIT_CORE,&core)) fail("no-core"); umask(0077);
        if(fchdir(directory)) fail("held-working-directory");
        write_exact(ready[1],"D",1); expect_byte(release[0],'R');
        close(ready[1]); close(release[0]); close(directory); private_stdio();
        /* All non-selected descriptors close on exec; only fixed empty stdio and
         * the CLOEXEC exec-error signal remain. No caller environment survives. */
        if(syscall(SYS_close_range,3u,~0u,4u)) fail("close-range-cloexec-required");
        char runtime[80],home[4096],login[512],shown[160];
        if(snprintf(runtime,sizeof(runtime),"XDG_RUNTIME_DIR=/run/user/%lu",(unsigned long)uid)>=(int)sizeof(runtime) ||
           snprintf(home,sizeof(home),"HOME=%s",account.pw_dir)>=(int)sizeof(home) ||
           snprintf(login,sizeof(login),"USER=%s",account.pw_name)>=(int)sizeof(login) ||
           snprintf(shown,sizeof(shown),"DISPLAY=%s",display)>=(int)sizeof(shown)) fail("bounded-child-environment");
        char *environment[]={runtime,home,login,"DOTNET_EnableDiagnostics=0","DOTNET_MULTILEVEL_LOOKUP=0","DOTNET_ROLL_FORWARD=Disable",
            !strcmp(session_type,"x11")?shown:"WAYLAND_DISPLAY=wayland-0",NULL};
        char *arguments[]={"Haven.Application",!strcmp(argv[1],"--original-home-v1")?"--home-linux-root-control":"--home-linux-root-host",(char *)endpoint,NULL};
        fexecve(image,arguments,environment);
        int effect_errno=errno; write_exact(executed[1],&effect_errno,sizeof(effect_errno)); _exit(71);
    }
    owned_child=child;
    close(ready[1]); close(release[0]); close(executed[1]); close(directory); close(image);
    int child_pidfd=(int)syscall(SYS_pidfd_open,child,0); if(child_pidfd<0) { kill(child,SIGKILL); fail("held-child-pidfd-required"); }
    expect_byte(ready[0],'N');
    char mapping[8192]; int used=snprintf(mapping,sizeof(mapping),"0 0 1\n%lu %lu 1\n",(unsigned long)uid,(unsigned long)uid);
    if(used<=0 || (size_t)used>=sizeof(mapping)) fail("uid-map-bound");
    proc_map(child,"uid_map",mapping);
    used=snprintf(mapping,sizeof(mapping),"0 0 1\n");
    for(int index=0;index<group_count;index++) {
        int duplicate=groups[index]==0;
        for(int previous=0;previous<index;previous++) if(groups[index]==groups[previous]) duplicate=1;
        if(duplicate) continue;
        int count=snprintf(mapping+used,sizeof(mapping)-(size_t)used,"%lu %lu 1\n",(unsigned long)groups[index],(unsigned long)groups[index]);
        if(count<=0 || (size_t)count>=sizeof(mapping)-(size_t)used) fail("gid-map-bound");
        used+=count;
    }
    proc_map(child,"gid_map",mapping); write_exact(release[1],"M",1); expect_byte(ready[0],'D');
    if(dprintf(STDOUT_FILENO,"OriginalLinuxHomeChildV1:%ld:%lu\n",(long)child,(unsigned long)uid)<0) fail("private-child-observation");
    expect_byte(STDIN_FILENO,'R'); write_exact(release[1],"R",1); close(release[1]); close(ready[0]);
    struct pollfd effect={.fd=executed[0],.events=POLLIN|POLLHUP}; int result;
    do { result=poll(&effect,1,15000); } while(result<0 && errno==EINTR);
    int effect_errno=0; ssize_t observed=result==1?read(executed[0],&effect_errno,sizeof(effect_errno)):-1; close(executed[0]);
    if(observed!=0) { kill(child,SIGKILL); errno=effect_errno; fail("fd-exec-unconfirmed"); }
    /* EOF alone also occurs on pre-exec death. Require the actual current kernel
     * executable to match the held signed image before reporting an exec. */
    char actual_path[96];
    if(snprintf(actual_path,sizeof(actual_path),"/proc/%ld/exe",(long)child)>=(int)sizeof(actual_path)) fail("post-exec-path");
    int actual_image=open(actual_path,O_RDONLY|O_CLOEXEC);
    struct stat actual_stat;
    if(actual_image<0 || fstat(actual_image,&actual_stat) || actual_stat.st_ino!=before.st_ino || actual_stat.st_dev!=before.st_dev ||
       actual_stat.st_size!=before.st_size || actual_stat.st_mtim.tv_sec!=before.st_mtim.tv_sec || actual_stat.st_mtim.tv_nsec!=before.st_mtim.tv_nsec ||
       actual_stat.st_ctim.tv_sec!=before.st_ctim.tv_sec || actual_stat.st_ctim.tv_nsec!=before.st_ctim.tv_nsec) fail("actual-post-exec-image-required");
    close(actual_image);
    if(dprintf(STDOUT_FILENO,"OriginalLinuxHomeExecV1\n")<0) fail("private-exec-observation");
    /* Do not reap/recycle the original numeric PID until this owner retires it.
     * The pidfd exit observation and stdin retirement are independent of child IO. */
    struct pollfd lifetime[2]={{.fd=child_pidfd,.events=POLLIN},{.fd=STDIN_FILENO,.events=POLLIN|POLLHUP}};
    do { result=poll(lifetime,2,-1); } while(result<0 && errno==EINTR);
    if(result<0) { kill(child,SIGKILL); fail("child-lifetime"); }
    int original_exit=(lifetime[0].revents&POLLIN)!=0;
    if(original_exit) {
        siginfo_t observed_exit; memset(&observed_exit,0,sizeof(observed_exit));
        if(waitid(P_PID,child,&observed_exit,WEXITED|WNOWAIT)) fail("original-child-exit-status");
        if(dprintf(STDOUT_FILENO,"OriginalLinuxHomeExitedV1:%d:%d\n",observed_exit.si_code,observed_exit.si_status)<0) fail("private-exit-observation");
        struct pollfd retirement={.fd=STDIN_FILENO,.events=POLLIN|POLLHUP};
        do { result=poll(&retirement,1,-1); } while(result<0 && errno==EINTR);
    } else kill(child,SIGKILL);
    int status; while(waitpid(child,&status,0)<0) { if(errno!=EINTR) fail("retained-child-reap"); }
    owned_child=-1;
    close(child_pidfd);
    if(original_exit) {
        if(WIFEXITED(status)) return WEXITSTATUS(status);
        if(WIFSIGNALED(status)) return 128+WTERMSIG(status);
        return 70;
    }
    return 0;
}
