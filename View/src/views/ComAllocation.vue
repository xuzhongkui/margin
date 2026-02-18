<script setup>
import { ref, onMounted } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import { Search, Plus, Edit, Delete, Refresh } from "@element-plus/icons-vue";
import {
  getAllComAllocations,
  createComAllocation,
  updateComAllocation,
  deleteComAllocation,
} from "../services/comAllocation";
import { getAllUsers } from "../services/users";
import { getComSnapshot, getConnectedDevices } from "../services/device";

// 分配列表数据
const tableData = ref([]);

// 搜索关键词
const searchKeyword = ref("");

// 对话框控制
const dialogVisible = ref(false);
const dialogTitle = ref("新增COM分配");
const formData = ref({
  id: "",
  userId: "",
  deviceId: "",
  comList: [],
});

// 加载状态
const loading = ref(false);

// 用户列表（用于下拉选择）
const userOptions = ref([]);

// 设备列表（用于下拉选择）
const deviceOptions = ref([]);
const loadingDevices = ref(false);

// 设备COM快照数据
const deviceComData = ref([]);
const loadingDeviceCom = ref(false);

// 格式化日期时间
const formatDateTime = (dateString) => {
  if (!dateString) return "";
  const date = new Date(dateString);
  return date.toLocaleString("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
};

// 加载用户列表（只加载非管理员用户）
const loadUsers = async () => {
  try {
    const users = await getAllUsers();
    // 过滤掉管理员（role === 1）
    userOptions.value = users.filter((user) => user.role !== 1);
  } catch (error) {
    ElMessage.error("加载用户列表失败: " + error.message);
  }
};

// 加载设备列表
const loadDevices = async () => {
  loadingDevices.value = true;
  try {
    const devices = await getConnectedDevices();
    deviceOptions.value = Array.isArray(devices) ? devices : [];
  } catch (error) {
    ElMessage.error("加载设备列表失败: " + error.message);
    deviceOptions.value = [];
  } finally {
    loadingDevices.value = false;
  }
};

// 加载COM分配列表
const loadAllocations = async () => {
  loading.value = true;
  try {
    const allocations = await getAllComAllocations();
    tableData.value = allocations;
  } catch (error) {
    ElMessage.error("加载COM分配列表失败: " + error.message);
  } finally {
    loading.value = false;
  }
};

// 加载设备COM快照
const loadDeviceComSnapshot = async (deviceId) => {
  if (!deviceId) {
    deviceComData.value = [];
    return;
  }

  loadingDeviceCom.value = true;
  try {
    const snapshot = await getComSnapshot(deviceId);
    deviceComData.value = Array.isArray(snapshot) ? snapshot : [];
  } catch (error) {
    ElMessage.error("加载设备COM信息失败: " + error.message);
    deviceComData.value = [];
  } finally {
    loadingDeviceCom.value = false;
  }
};

// 搜索
const handleSearch = () => {
  if (!searchKeyword.value) {
    loadAllocations();
    return;
  }

  const keyword = searchKeyword.value.toLowerCase();
  const filtered = tableData.value.filter(
    (item) =>
      item.userName?.toLowerCase().includes(keyword) ||
      item.deviceId?.toLowerCase().includes(keyword)
  );
  tableData.value = filtered;
  ElMessage.success(`找到 ${filtered.length} 条记录`);
};

// 刷新列表
const handleRefresh = async () => {
  searchKeyword.value = "";
  await loadAllocations();
  ElMessage.success("刷新成功");
};

// 新增分配
const handleAdd = () => {
  dialogTitle.value = "新增COM分配";
  formData.value = {
    id: "",
    userId: "",
    deviceId: "",
    comList: [],
  };
  deviceComData.value = [];
  dialogVisible.value = true;
};

// 编辑分配
const handleEdit = async (row) => {
  dialogTitle.value = "编辑COM分配";
  formData.value = {
    id: row.id,
    userId: row.userId,
    deviceId: row.deviceId,
    comList: Array.isArray(row.comList) ? [...row.comList] : [],
  };

  // 加载设备COM快照
  await loadDeviceComSnapshot(row.deviceId);
  dialogVisible.value = true;
};

// 删除分配
const handleDelete = async (row) => {
  try {
    await ElMessageBox.confirm(
      `确定要删除用户 "${row.userName}" 的COM分配吗？此操作不可恢复。`,
      "删除确认",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: "warning",
      }
    );

    loading.value = true;
    await deleteComAllocation(row.id);
    ElMessage.success("删除成功");
    await loadAllocations();
  } catch (error) {
    if (error !== "cancel") {
      ElMessage.error("删除失败: " + error.message);
    }
  } finally {
    loading.value = false;
  }
};

// 保存分配
const handleSave = async () => {
  // 验证必填项
  if (!formData.value.userId) {
    ElMessage.warning("请选择用户");
    return;
  }

  if (!formData.value.deviceId) {
    ElMessage.warning("请输入设备ID");
    return;
  }

  if (!formData.value.comList || formData.value.comList.length === 0) {
    ElMessage.warning("请至少选择一个COM端口");
    return;
  }

  loading.value = true;
  try {
    if (formData.value.id) {
      // 编辑
      await updateComAllocation(formData.value.id, {
        userId: formData.value.userId,
        deviceId: formData.value.deviceId,
        comList: formData.value.comList,
      });
      ElMessage.success("更新成功");
    } else {
      // 新增
      await createComAllocation({
        userId: formData.value.userId,
        deviceId: formData.value.deviceId,
        comList: formData.value.comList,
      });
      ElMessage.success("创建成功");
    }
    dialogVisible.value = false;
    await loadAllocations();
  } catch (error) {
    ElMessage.error("保存失败: " + error.message);
  } finally {
    loading.value = false;
  }
};

// 设备ID变化时加载COM快照
const handleDeviceIdChange = async (deviceId) => {
  formData.value.comList = [];
  await loadDeviceComSnapshot(deviceId);
};

// 获取用户名
const getUserName = (userId) => {
  const user = userOptions.value.find((u) => u.id === userId);
  return user ? user.userName : userId;
};

onMounted(async () => {
  await loadUsers();
  await loadDevices();
  await loadAllocations();
});
</script>

<template>
  <div class="com-allocation">
    <div class="content-wrapper">
      <el-card shadow="never" class="search-card">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-input
              v-model="searchKeyword"
              placeholder="搜索用户名或设备ID"
              clearable
              @keyup.enter="handleSearch"
            >
              <template #prefix>
                <el-icon><Search /></el-icon>
              </template>
            </el-input>
          </el-col>
          <el-col :span="16">
            <el-space>
              <el-button type="primary" :icon="Search" @click="handleSearch">
                搜索
              </el-button>
              <el-button :icon="Refresh" @click="handleRefresh">刷新</el-button>
              <el-button type="success" :icon="Plus" @click="handleAdd">
                新增分配
              </el-button>
            </el-space>
          </el-col>
        </el-row>
      </el-card>

      <el-card shadow="never" class="table-card">
        <el-table :data="tableData" v-loading="loading" stripe>
          <el-table-column prop="id" label="ID" width="280" />
          <el-table-column prop="userName" label="用户名" width="150">
            <template #default="{ row }">
              {{ getUserName(row.userId) }}
            </template>
          </el-table-column>
          <el-table-column prop="deviceId" label="设备ID" width="200" />
          <el-table-column prop="comList" label="分配的COM端口" min-width="200">
            <template #default="{ row }">
              <el-tag
                v-for="com in row.comList"
                :key="com"
                size="small"
                style="margin-right: 4px"
              >
                {{ com }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="createTime" label="创建时间" width="180">
            <template #default="{ row }">
              {{ formatDateTime(row.createTime) }}
            </template>
          </el-table-column>
          <el-table-column prop="updateTime" label="更新时间" width="180">
            <template #default="{ row }">
              {{ formatDateTime(row.updateTime) }}
            </template>
          </el-table-column>
          <el-table-column label="操作" fixed="right" width="180">
            <template #default="{ row }">
              <el-button
                type="primary"
                size="small"
                :icon="Edit"
                link
                @click="handleEdit(row)"
              >
                编辑
              </el-button>
              <el-button
                type="danger"
                size="small"
                :icon="Delete"
                link
                @click="handleDelete(row)"
              >
                删除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <!-- 新增/编辑对话框 -->
      <el-dialog
        v-model="dialogVisible"
        :title="dialogTitle"
        width="600px"
        :close-on-click-modal="false"
        align-center
      >
        <el-form :model="formData" label-width="auto" class="dialog-form">
          <el-form-item label="用户" required>
            <el-select
              v-model="formData.userId"
              placeholder="请选择用户"
              style="width: 100%"
              filterable
            >
              <el-option
                v-for="user in userOptions"
                :key="user.id"
                :label="user.userName"
                :value="user.id"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="设备ID" required>
            <el-select
              v-model="formData.deviceId"
              placeholder="请选择设备"
              style="width: 100%"
              filterable
              :loading="loadingDevices"
              @change="handleDeviceIdChange"
            >
              <el-option
                v-for="device in deviceOptions"
                :key="device"
                :label="device"
                :value="device"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="COM端口" required>
            <el-select
              v-model="formData.comList"
              placeholder="请选择COM端口"
              style="width: 100%"
              multiple
              filterable
              :loading="loadingDeviceCom"
            >
              <el-option
                v-for="com in deviceComData"
                :key="com.portName"
                :label="`${com.portName} ${com.isSmsModem ? '(短信猫)' : ''} ${
                  com.modemInfo?.operator || ''
                }`"
                :value="com.portName"
              />
            </el-select>
            <div
              v-if="deviceComData.length === 0 && formData.deviceId"
              style="margin-top: 8px"
            >
              <el-text type="info" size="small">
                该设备暂无COM端口信息，请先在COM扫描页面扫描设备
              </el-text>
            </div>
          </el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="dialogVisible = false">取消</el-button>
          <el-button type="primary" @click="handleSave">保存</el-button>
        </template>
      </el-dialog>
    </div>
  </div>
</template>

<style scoped>
.com-allocation {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.content-wrapper {
  flex: 1;
  padding: 24px;
  overflow-y: auto;
}

.search-card {
  margin-bottom: 16px;
}

.table-card {
  min-height: 400px;
}

.el-table {
  margin-top: 16px;
}
</style>
