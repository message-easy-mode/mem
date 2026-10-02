# MEM CLI bootstrap payload

Release packaging may place these files beside this README:

- `install-host-command.sh` — copied from `cli/src/Mem.Cli/Scripts/install-host-command.sh`.
- `mem` — the published MEM CLI single-file Linux binary.

During bootstrap, `install.sh` stages those files under `/opt/mem/bootstrap/cli` and installs the stable host command:

```text
/opt/mem/cli/<version>/mem
/usr/local/bin/mem -> /opt/mem/cli/<version>/mem
```

Source-tree development can pass a published binary explicitly:

```bash
sudo ./bootstrap/install.sh --mem-cli-binary /path/to/published/mem --mem-cli-version dev
```
