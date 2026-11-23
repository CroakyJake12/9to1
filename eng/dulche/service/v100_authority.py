#!/usr/bin/python3
"""Per-boot, single-device VFIO broker. Install a reviewed copy under /run before launching as root."""
import argparse
import contextlib
import fcntl
import json
import os
from pathlib import Path
import pwd
import select
import signal
import socket
import stat
import struct
import subprocess
import time

PCI = Path("/sys/bus/pci/devices/0000:05:00.0")
PCI_ID = "0000:05:00.0"
ROOT = Path("/run/dulche-v100")
SOCKET = ROOT/"control.sock"
VM_UNIT = "astra-v100-vm.service"
STABLE_HELPER = ROOT/"authority.py"
DRIVER = Path("/sys/bus/pci/drivers/vfio-pci")


def need(value,message):
    if not value:
        raise RuntimeError(message)


def identity():
    need(PCI.exists(),"V100 PCI function is absent")
    for name,value in (("vendor","0x10de"),("device","0x1db4"),("subsystem_vendor","0x10de"),("subsystem_device","0x1214")):
        need((PCI/name).read_text().strip()==value,"Unexpected V100 PCI identity")
    group=(PCI/"iommu_group").resolve(strict=True)
    need(sorted(x.name for x in (group/"devices").iterdir())==[PCI_ID],"V100 IOMMU group is shared")
    bus=PCI.resolve(strict=True).parent
    need(bus.name=="0000:00:1c.4","Unexpected V100 upstream port")
    functions=[n.name for n in bus.rglob("????:??:??.?") if (n/"vendor").is_file()]
    need(functions==[PCI_ID],"V100 reset domain contains other PCI functions")
    need((Path("/sys/bus/pci/devices/0000:01:00.0/driver").resolve(strict=True).name=="nvidia"),
         "RTX display driver is not in its expected state")
    return Path("/dev/vfio")/group.name


def driver():
    return (PCI/"driver").resolve().name if (PCI/"driver").exists() else None


def attach(uid,gid):
    node=identity()
    current=driver()
    need(current in (None,"vfio-pci"),"V100 is owned by an unexpected driver; refusing to unbind")
    if not current:
        override=(PCI/"driver_override").read_text().strip()
        need(override in ("","(null)","vfio-pci"),"V100 carries a foreign driver override")
        subprocess.run(["/usr/sbin/modprobe","vfio-pci"],stdin=subprocess.DEVNULL,check=True,timeout=10,
                       env={"PATH":"/usr/sbin:/usr/bin:/sbin:/bin"})
        (PCI/"driver_override").write_text("vfio-pci")
        try:
            (DRIVER/"bind").write_text(PCI_ID)
        except BaseException:
            if driver() is None:
                (PCI/"driver_override").write_text("\n")
            raise
    for _ in range(20):
        if node.exists():break
        time.sleep(.1)
    need(driver()=="vfio-pci" and stat.S_ISCHR(node.lstat().st_mode),"VFIO group did not appear")
    # /dev/vfio is controlled by root and the name came from the verified IOMMU group.
    os.chown(node,uid,gid,follow_symlinks=False)
    os.chmod(node,0o600)


def group_holders(node):
    key=node.stat().st_rdev
    result=[]
    for process in Path("/proc").glob("[0-9]*"):
        for fd in list_safe(process/"fd"):
            try:
                st=fd.stat()
                if stat.S_ISCHR(st.st_mode) and st.st_rdev==key:
                    result.append(process)
                    break
            except (FileNotFoundError,ProcessLookupError):
                continue
    return result


def list_safe(directory):
    try:
        return list(directory.iterdir())
    except (FileNotFoundError,ProcessLookupError):
        return []


def terminate_owned_guest(process,uid):
    """Pin the PID before reading identity; never signal a recycled PID or another VM."""
    pidfd=os.pidfd_open(int(process.name))
    try:
        if select.select([pidfd],[],[],0)[0]:
            return
        need(process.stat().st_uid==uid and process.joinpath("exe").resolve()==Path("/usr/bin/qemu-system-x86_64").resolve(),
             "Another process owns the VFIO group")
        args=process.joinpath("cmdline").read_bytes().split(b"\0")
        need(b"-name" in args and args[args.index(b"-name")+1]==b"strata-v100-vm","Foreign QEMU owns the VFIO group")
        row=next((line[3:] for line in process.joinpath("cgroup").read_text().splitlines() if line.startswith("0::")),"")
        need(row==f"/user.slice/user-{uid}.slice/user@{uid}.service/app.slice/{VM_UNIT}",
             "QEMU is not in the approved V100-only service cgroup")
        for sig,timeout in ((signal.SIGTERM,4),(signal.SIGKILL,2)):
            if select.select([pidfd],[],[],0)[0]:
                return
            signal.pidfd_send_signal(pidfd,sig)
            if select.select([pidfd],[],[],timeout)[0]:
                return
        raise RuntimeError("V100 guest could not be stopped; not unbinding")
    finally:
        os.close(pidfd)


def detach(uid):
    node=identity()
    need(driver() in (None,"vfio-pci"),"Unexpected driver; refusing detach")
    if driver()=="vfio-pci":
        holders=group_holders(node)
        need(len(holders)<=1,"Multiple processes hold the VFIO group; refusing to signal a process")
        for holder in holders:
            terminate_owned_guest(holder,uid)
        need(not group_holders(node),"VFIO remains in use")
        (DRIVER/"unbind").write_text(PCI_ID)
    (PCI/"driver_override").write_text("\n")
    need(driver() is None,"V100 is still bound")


def serve(account):
    need(os.geteuid()==0,"Administrator authorization is required")
    for path in (ROOT,STABLE_HELPER):
        st=path.lstat()
        need(st.st_uid==0 and not st.st_mode&0o022 and not path.is_symlink(),"Only a root-owned reviewed runtime copy may run")
    need(Path(__file__).resolve()==STABLE_HELPER and stat.S_ISDIR(ROOT.stat().st_mode) and
         stat.S_ISREG(STABLE_HELPER.stat().st_mode),"Invalid installed helper path")
    owner=pwd.getpwnam(account)
    need(owner.pw_uid>=1000,"An ordinary local account is required")
    identity()
    with (ROOT/"authority.lock").open("a") as lock,socket.socket(socket.AF_UNIX,socket.SOCK_SEQPACKET) as server:
        fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
        SOCKET.unlink(missing_ok=True)
        server.bind(str(SOCKET))
        os.chown(SOCKET,owner.pw_uid,owner.pw_gid)
        os.chmod(SOCKET,0o600);server.listen(4)
        print("V100 authority ready for",account,flush=True)
        while True:
            peer,_=server.accept()
            with peer:
                try:
                    peer.settimeout(2)
                    _,uid,_=struct.unpack("3i",peer.getsockopt(socket.SOL_SOCKET,socket.SO_PEERCRED,12))
                    need(uid==owner.pw_uid,"Unrecognized local owner")
                    packet=peer.recv(32)
                    need(packet in (b"status",b"attach",b"detach"),"Unknown operation")
                    if packet==b"attach":attach(owner.pw_uid,owner.pw_gid)
                    elif packet==b"detach":detach(owner.pw_uid)
                    group=identity()
                    out={"ok":True,"pci":PCI_ID,"group":group.name,"driver":driver()}
                    if packet!=b"status":print(packet.decode(),out,flush=True)
                except (OSError,ValueError,RuntimeError,subprocess.SubprocessError) as exc:
                    out={"ok":False,"error":str(exc)}
                    print("Refused operation:",str(exc),flush=True)
                with contextlib.suppress(OSError):
                    peer.sendall(json.dumps(out).encode())


def request(command):
    with socket.socket(socket.AF_UNIX,socket.SOCK_SEQPACKET) as s:
        s.settimeout(12);s.connect(str(SOCKET))
        _,uid,_=struct.unpack("3i",s.getsockopt(socket.SOL_SOCKET,socket.SO_PEERCRED,12))
        need(uid==0,"The VFIO authority does not belong to root")
        s.sendall(command.encode())
        result=json.loads(s.recv(2048))
        need(result.get("ok") is True,result.get("error","Invalid authority response"))
        return result


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serve-account")
    parser.add_argument("--inspect",action="store_true")
    args=parser.parse_args()
    if args.inspect:
        print(json.dumps({"pci":PCI_ID,"group":identity().name,"driver":driver()}))
    elif args.serve_account:
        serve(args.serve_account)
    else:
        parser.error("Specify --inspect or --serve-account")
