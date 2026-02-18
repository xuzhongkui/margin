<script setup>
import {
  nextTick,
  onBeforeUnmount,
  onMounted,
  reactive,
  ref,
  watch,
} from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import { Refresh } from "@element-plus/icons-vue";
import { createDeviceHubConnection } from "@/services/signalr";
import {
  getComSnapshot,
  getConnectedDevices,
  triggerComPortScan,
  upsertComSnapshot,
} from "@/services/device";

const form = reactive({
  deviceId: "",
});

const deviceOptions = ref([]);
const isLoadingDevices = ref(false);

const loadConnectedDeviceOptions = async () => {
  isLoadingDevices.value = true;
  try {
    const list = await getConnectedDevices();
    deviceOptions.value = Array.isArray(list) ? list : [];

    // 若当前选择的 deviceId 不在列表里，保留（可能是手动输入/刚掉线），但在 UI 上仍可继续使用。
    if (
      form.deviceId &&
      !deviceOptions.value.some((d) => String(d) === String(form.deviceId))
    ) {
      deviceOptions.value = [form.deviceId, ...deviceOptions.value];
    }
  } catch (e) {
    ElMessage.error(`加载已连接设备失败：${e?.message || e}`);
  } finally {
    isLoadingDevices.value = false;
  }
};

const isConnecting = ref(false);
const isConnected = ref(false);

// 扫描幂等：扫描进行中不允许再次触发（直到收到 Completed 或发生错误）。
const isScanning = ref(false);
const ports = ref([]);
const lastCompletedAt = ref("");

// 编辑/删除：当前正在编辑的行（浅拷贝），保存后回写到 ports
const editDialogVisible = ref(false);
const editForm = reactive({
  _originalPortName: "",
  deviceId: "",
  portName: "",
  isAvailable: false,
  isSmsModem: false,
  modemInfo: {
    phoneNumber: "",
    operator: "",
    iccid: "",
  },
});

const openEditDialog = (row) => {
  editForm.deviceId = String(row?.deviceId || form.deviceId || "");
  editForm._originalPortName = String(row?.portName || "");
  editForm.portName = String(row?.portName || "");
  editForm.isAvailable = Boolean(row?.isAvailable);
  editForm.isSmsModem = Boolean(row?.isSmsModem);

  const mi = row?.modemInfo || {};
  editForm.modemInfo.phoneNumber = String(mi.phoneNumber || "");
  editForm.modemInfo.operator = String(mi.operator || "");
  editForm.modemInfo.iccid = String(mi.iccid || "");

  editDialogVisible.value = true;
};

const normalizeRowForSave = (row) => {
  const normalized = { ...row };
  normalized.deviceId = String(normalized.deviceId || form.deviceId || "");
  normalized.portName = String(normalized.portName || "");

  // 保持为 boolean（前端展示/编辑用）
  normalized.isAvailable = Boolean(normalized.isAvailable);
  normalized.isSmsModem = Boolean(normalized.isSmsModem);

  const modemInfo = normalized.modemInfo ? { ...normalized.modemInfo } : {};
  modemInfo.phoneNumber = String(modemInfo.phoneNumber || "");
  modemInfo.operator = String(modemInfo.operator || "");
  modemInfo.iccid = String(modemInfo.iccid || "");
  normalized.modemInfo = modemInfo;

  return normalized;
};

const saveSnapshotToServer = async () => {
  if (!form.deviceId) {
    ElMessage.warning("请先选择设备");
    return;
  }

  // 只保存当前 deviceId 的行
  const deviceRows = ports.value
    .filter((p) => String(p?.deviceId) === String(form.deviceId))
    .map((p) => normalizeRowForSave(p));

  await upsertComSnapshot(form.deviceId, deviceRows);
};

const handleEditSave = async () => {
  if (!form.deviceId) {
    ElMessage.warning("请先选择设备");
    return;
  }

  if (!editForm.portName) {
    ElMessage.warning("端口不能为空");
    return;
  }

  const idx = ports.value.findIndex(
    (p) =>
      String(p?.deviceId) === String(editForm.deviceId || form.deviceId) &&
      String(p?.portName) === String(editForm._originalPortName)
  );

  if (idx >= 0) {
    ports.value[idx] = normalizeRowForSave({
      ...ports.value[idx],
      portName: editForm.portName,
      isAvailable: editForm.isAvailable,
      isSmsModem: editForm.isSmsModem,
      modemInfo: {
        ...(ports.value[idx]?.modemInfo || {}),
        phoneNumber: editForm.modemInfo.phoneNumber,
        operator: editForm.modemInfo.operator,
        iccid: editForm.modemInfo.iccid,
      },
    });
  } else {
    ports.value.push(
      normalizeRowForSave({
        deviceId: editForm.deviceId || form.deviceId,
        portName: editForm.portName,
        isAvailable: editForm.isAvailable,
        isSmsModem: editForm.isSmsModem,
        modemInfo: {
          phoneNumber: editForm.modemInfo.phoneNumber,
          operator: editForm.modemInfo.operator,
          iccid: editForm.modemInfo.iccid,
        },
      })
    );
  }

  // 保存成功前后，统一把 original key 更新为当前值
  editForm._originalPortName = String(editForm.portName);

  try {
    await saveSnapshotToServer();
    ElMessage.success("已保存");
    editDialogVisible.value = false;
  } catch (e) {
    ElMessage.error(`保存失败：${e?.message || e}`);
  }
};

const handleSaveAll = async () => {
  if (!form.deviceId) {
    ElMessage.warning("请先选择设备");
    return;
  }

  try {
    await saveSnapshotToServer();
    ElMessage.success("保存成功");
  } catch (e) {
    ElMessage.error(`保存失败：${e?.message || e}`);
  }
};

const handleDeleteRow = async (row) => {
  if (!form.deviceId) {
    ElMessage.warning("请先选择设备");
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确定要删除端口 "${row?.portName || ""}" 吗？`,
      "删除确认",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: "warning",
      }
    );

    ports.value = ports.value.filter(
      (p) =>
        !(
          String(p?.deviceId) === String(form.deviceId) &&
          String(p?.portName) === String(row?.portName)
        )
    );

    await saveSnapshotToServer();
    ElMessage.success("已删除");
  } catch (e) {
    if (e !== "cancel") {
      ElMessage.error(`删除失败：${e?.message || e}`);
    }
  }
};

const loadSnapshotFromServer = async () => {
  if (!form.deviceId) return;

  try {
    const list = await getComSnapshot(form.deviceId);
    const rows = Array.isArray(list) ? list : [];

    // 合并：把当前设备的扫描结果替换为服务器快照（便于编辑/删除后的展示）
    ports.value = ports.value.filter(
      (p) => String(p?.deviceId) !== String(form.deviceId)
    );

    ports.value.push(...rows.map((r) => normalizeRowForSave(r)));
  } catch (e) {
    // 不打断页面，仅提示
    ElMessage.error(`加载已保存 COM 信息失败：${e?.message || e}`);
  }
};

// 让滚动发生在表格 tbody（Element Plus 的 table body），避免整张卡片滚动。
const tableHeight = ref(320);
const tableWrapElRef = ref(null);

const updateTableHeight = async () => {
  await nextTick();

  const el = tableWrapElRef.value;
  if (!el || typeof window === "undefined") return;

  const rect = el.getBoundingClientRect();
  // 预留底部空间，避免出现页面（外层）滚动条，只保留表格内部滚动。
  const bottomPadding = 96;
  const max = Math.max(
    220,
    Math.floor(window.innerHeight - rect.top - bottomPadding)
  );
  tableHeight.value = max;
};

let connection = null;

const reset = () => {
  ports.value = [];
  lastCompletedAt.value = "";
  updateTableHeight();
};

const connect = async () => {
  if (isConnected.value || isConnecting.value) return;

  isConnecting.value = true;
  try {
    connection = createDeviceHubConnection();

    connection.on("ComPortFound", (deviceId, comPortJson) => {
      if (form.deviceId && deviceId !== form.deviceId) return;

      const upsert = (row) => {
        const keyPort = row?.portName;
        if (!keyPort) {
          ports.value.push(row);
          return;
        }

        const idx = ports.value.findIndex(
          (p) => p.deviceId === deviceId && p.portName === keyPort
        );
        if (idx >= 0) {
          ports.value[idx] = { ...ports.value[idx], ...row };
        } else {
          ports.value.push(row);
        }
      };

      try {
        const info = JSON.parse(comPortJson);
        upsert({
          deviceId,
          ...info,
        });
      } catch {
        upsert({
          deviceId,
          raw: comPortJson,
        });
      }
    });

    connection.on("ComPortScanCompleted", (deviceId, scanTimeIso) => {
      if (form.deviceId && deviceId !== form.deviceId) return;
      lastCompletedAt.value = scanTimeIso;
      isScanning.value = false;
    });

    await connection.start();
    isConnected.value = true;
    ElMessage.success("已连接到即时通讯");
  } catch (e) {
    ElMessage.error(`连接失败：${e?.message || e}`);
    isConnected.value = false;
    connection = null;
  } finally {
    isConnecting.value = false;
    updateTableHeight();
  }
};

const disconnect = async () => {
  if (!connection) return;

  try {
    await connection.stop();
  } finally {
    connection = null;
    isConnected.value = false;
    isScanning.value = false;
  }
};

const startScan = async () => {
  if (isScanning.value) {
    ElMessage.warning("正在扫描中，请等待扫描完成");
    return;
  }

  if (!form.deviceId) {
    ElMessage.warning("请先选择设备");
    return;
  }

  if (!isConnected.value) {
    await connect();
  }

  // connect 失败会把 isConnected 置为 false；此时不再继续触发扫描。
  if (!isConnected.value) return;

  reset();
  isScanning.value = true;

  try {
    await triggerComPortScan(form.deviceId);
    ElMessage.success("已发送扫描请求");
  } catch (e) {
    isScanning.value = false;
    ElMessage.error(`发送扫描请求失败：${e?.message || e}`);
  } finally {
    updateTableHeight();
  }
};

watch(
  () => form.deviceId,
  async () => {
    // 切换设备后清空旧数据，避免误以为仍是当前设备的结果。
    reset();
    await loadSnapshotFromServer();
  }
);

onMounted(async () => {
  window.addEventListener("resize", updateTableHeight);

  await loadConnectedDeviceOptions();

  // 页面进入时自动连接，避免错过实时推送。
  await connect();

  // 若初始化时已经有 deviceId（如缓存/路由回填），尝试加载已保存快照
  await loadSnapshotFromServer();

  updateTableHeight();
});

onBeforeUnmount(async () => {
  window.removeEventListener("resize", updateTableHeight);
  await disconnect();
});

watch(lastCompletedAt, () => {
  updateTableHeight();
});
</script>

<template>
  <el-card class="app-card comscan-card">
    <template #header>
      <div class="comscan-header">
        <span class="card-title">COM 扫描</span>
        <el-tag :type="isConnected ? 'success' : 'info'">
          {{ isConnected ? "已连接" : isConnecting ? "连接中" : "未连接" }}
        </el-tag>
      </div>
    </template>

    <el-form :inline="true" label-width="80px">
      <el-form-item label="deviceId">
        <el-select
          v-model="form.deviceId"
          filterable
          clearable
          placeholder="请选择已连接设备"
          :loading="isLoadingDevices"
          style="width: 260px"
        >
          <el-option
            v-for="d in deviceOptions"
            :key="String(d)"
            :label="String(d)"
            :value="String(d)"
          />
        </el-select>
        <el-button
          :icon="Refresh"
          :loading="isLoadingDevices"
          style="margin-left: 8px"
          @click="loadConnectedDeviceOptions"
        >
          刷新
        </el-button>
      </el-form-item>

      <el-form-item>
        <el-button
          type="primary"
          :loading="isScanning"
          :disabled="isScanning"
          @click="startScan"
        >
          开始扫描
        </el-button>
        <el-button type="success" @click="handleSaveAll"> 保存全部 </el-button>
        <el-button @click="reset">清空</el-button>
        <el-button :disabled="isConnected || isConnecting" @click="connect">
          连接
        </el-button>
        <el-button :disabled="!isConnected" type="danger" @click="disconnect">
          断开
        </el-button>
      </el-form-item>
    </el-form>

    <div v-if="lastCompletedAt" class="comscan-info">
      <el-text type="info">扫描完成时间：{{ lastCompletedAt }}</el-text>
    </div>

    <div ref="tableWrapElRef" class="comscan-table-wrap">
      <el-table
        :data="ports"
        :height="tableHeight"
        style="width: 100%"
        size="small"
      >
        <el-table-column prop="deviceId" label="设备" width="180" />
        <el-table-column prop="portName" label="端口" width="120" />

        <el-table-column prop="isAvailable" label="可用" width="80">
          <template #default="scope">
            <el-tag :type="scope.row.isAvailable ? 'success' : 'info'">
              {{ scope.row.isAvailable ? "是" : "否" }}
            </el-tag>
          </template>
        </el-table-column>

        <el-table-column prop="isSmsModem" label="短信猫" width="90">
          <template #default="scope">
            <el-tag :type="scope.row.isSmsModem ? 'success' : 'info'">
              {{ scope.row.isSmsModem ? "是" : "否" }}
            </el-tag>
          </template>
        </el-table-column>

        <el-table-column label="SIM" width="90">
          <template #default="scope">
            <el-tag
              v-if="scope.row.modemInfo"
              :type="scope.row.modemInfo.hasSimCard ? 'success' : 'danger'"
            >
              {{ scope.row.modemInfo.hasSimCard ? "有" : "无" }}
            </el-tag>
            <el-text v-else type="info">-</el-text>
          </template>
        </el-table-column>

        <el-table-column label="ICCID" width="210">
          <template #default="scope">
            <el-text v-if="scope.row.modemInfo">
              {{ scope.row.modemInfo.iccid || "-" }}
            </el-text>
            <el-text v-else type="info">-</el-text>
          </template>
        </el-table-column>

        <el-table-column label="运营商" width="140">
          <template #default="scope">
            <el-text v-if="scope.row.modemInfo">
              {{ scope.row.modemInfo.operator || "-" }}
            </el-text>
            <el-text v-else type="info">-</el-text>
          </template>
        </el-table-column>

        <el-table-column label="信号" width="140">
          <template #default="scope">
            <div v-if="scope.row.modemInfo">
              <el-text>
                {{
                  typeof scope.row.modemInfo.signalStrength === "number"
                    ? `${scope.row.modemInfo.signalStrength} (${
                        scope.row.modemInfo.signalQuality || "-"
                      })`
                    : "-"
                }}
              </el-text>
            </div>
            <el-text v-else type="info">-</el-text>
          </template>
        </el-table-column>

        <el-table-column label="号码" width="140">
          <template #default="scope">
            <el-text v-if="scope.row.modemInfo">
              {{ scope.row.modemInfo.phoneNumber || "-" }}
            </el-text>
            <el-text v-else type="info">-</el-text>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="160" fixed="right">
          <template #default="scope">
            <el-button
              size="small"
              type="primary"
              @click="openEditDialog(scope.row)"
            >
              编辑
            </el-button>
            <el-button
              size="small"
              type="danger"
              @click="handleDeleteRow(scope.row)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>

        <el-table-column label="详情">
          <template #default="scope">
            <div v-if="scope.row.modemInfo">
              <el-text
                >厂商：{{ scope.row.modemInfo.manufacturer || "-" }}</el-text
              >
              <el-divider direction="vertical" />
              <el-text>型号：{{ scope.row.modemInfo.model || "-" }}</el-text>
              <el-divider direction="vertical" />
              <el-text>IMEI：{{ scope.row.modemInfo.imei || "-" }}</el-text>
              <el-divider direction="vertical" />
              <el-text
                >SIM状态：{{ scope.row.modemInfo.simStatus || "-" }}</el-text
              >
              <el-divider direction="vertical" />
              <el-text
                >网络：{{ scope.row.modemInfo.networkStatus || "-" }}</el-text
              >
            </div>
            <div v-else-if="scope.row.raw">
              <el-text type="info">{{ scope.row.raw }}</el-text>
            </div>
            <div v-else>
              <el-text type="info">-</el-text>
            </div>
          </template>
        </el-table-column>
      </el-table>

      <el-dialog v-model="editDialogVisible" title="编辑端口" width="520px">
        <el-form label-width="100px">
          <el-form-item label="端口">
            <el-input v-model="editForm.portName" placeholder="例如 COM3" />
          </el-form-item>

          <el-form-item label="可用">
            <el-switch v-model="editForm.isAvailable" />
          </el-form-item>

          <el-form-item label="短信猫">
            <el-switch v-model="editForm.isSmsModem" />
          </el-form-item>

          <el-divider />

          <el-form-item label="号码">
            <el-input
              v-model="editForm.modemInfo.phoneNumber"
              placeholder="可选"
            />
          </el-form-item>

          <el-form-item label="运营商">
            <el-input
              v-model="editForm.modemInfo.operator"
              placeholder="可选"
            />
          </el-form-item>

          <el-form-item label="ICCID">
            <el-input v-model="editForm.modemInfo.iccid" placeholder="可选" />
          </el-form-item>
        </el-form>

        <template #footer>
          <el-button @click="editDialogVisible = false">取消</el-button>
          <el-button type="primary" @click="handleEditSave">保存</el-button>
        </template>
      </el-dialog>
    </div>
  </el-card>
</template>

<style scoped>
.comscan-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.comscan-info {
  margin: 8px 0;
}
.comscan-table-wrap {
  /* 只作为计算 max-height 的定位锚点；滚动由 el-table 的 body 承担 */
  width: 100%;
}
</style>
