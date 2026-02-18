<script setup>
import { ref, onMounted } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import { Search, Plus, Edit, Delete, Refresh } from "@element-plus/icons-vue";
import {
  getAllUsers,
  searchUsers,
  createUser,
  updateUser,
  deleteUser,
} from "../services/users";

// 用户列表数据
const tableData = ref([]);

// 搜索关键词
const searchKeyword = ref("");

// 对话框控制
const dialogVisible = ref(false);
const dialogTitle = ref("新增用户");
const formData = ref({
  id: "",
  userName: "",
  password: "",
  role: 0, // 0: User, 1: Admin
  remark: "",
});

// 加载状态
const loading = ref(false);

// 角色映射
const roleMap = {
  0: "普通用户",
  1: "管理员",
};

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

// 加载用户列表
const loadUsers = async () => {
  loading.value = true;
  try {
    const users = await getAllUsers();
    tableData.value = users;
  } catch (error) {
    ElMessage.error("加载用户列表失败: " + error.message);
  } finally {
    loading.value = false;
  }
};

// 搜索用户
const handleSearch = async () => {
  loading.value = true;
  try {
    const users = await searchUsers(searchKeyword.value);
    tableData.value = users;
    ElMessage.success(`找到 ${users.length} 个用户`);
  } catch (error) {
    ElMessage.error("搜索失败: " + error.message);
  } finally {
    loading.value = false;
  }
};

// 刷新列表
const handleRefresh = async () => {
  searchKeyword.value = "";
  await loadUsers();
  ElMessage.success("刷新成功");
};

// 新增用户
const handleAdd = () => {
  dialogTitle.value = "新增用户";
  formData.value = {
    id: "",
    userName: "",
    password: "",
    role: 0,
    remark: "",
  };
  dialogVisible.value = true;
};

// 编辑用户
const handleEdit = (row) => {
  dialogTitle.value = "编辑用户";
  formData.value = {
    id: row.id,
    userName: row.userName,
    password: "", // 编辑时密码为空，表示不修改
    role: row.role,
    remark: row.remark || "",
  };
  dialogVisible.value = true;
};

// 删除用户
const handleDelete = async (row) => {
  try {
    await ElMessageBox.confirm(
      `确定要删除用户 "${row.userName}" 吗？此操作不可恢复。`,
      "删除确认",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: "warning",
      }
    );

    loading.value = true;
    await deleteUser(row.id);
    ElMessage.success("删除成功");
    await loadUsers();
  } catch (error) {
    if (error !== "cancel") {
      ElMessage.error("删除失败: " + error.message);
    }
  } finally {
    loading.value = false;
  }
};

// 保存用户
const handleSave = async () => {
  // 验证必填项
  if (!formData.value.userName) {
    ElMessage.warning("请输入用户名");
    return;
  }

  // 新增时密码必填
  if (!formData.value.id && !formData.value.password) {
    ElMessage.warning("请输入密码");
    return;
  }

  // 密码长度验证
  if (formData.value.password && formData.value.password.length < 6) {
    ElMessage.warning("密码长度至少为6位");
    return;
  }

  loading.value = true;
  try {
    if (formData.value.id) {
      // 编辑用户
      const updateData = {
        userName: formData.value.userName,
        role: formData.value.role,
        remark: formData.value.remark,
      };
      // 如果输入了新密码，则包含密码字段
      if (formData.value.password) {
        updateData.newPassword = formData.value.password;
      }
      await updateUser(formData.value.id, updateData);
      ElMessage.success("更新成功");
    } else {
      // 新增用户
      await createUser({
        userName: formData.value.userName,
        password: formData.value.password,
        role: formData.value.role,
        remark: formData.value.remark,
      });
      ElMessage.success("创建成功");
    }
    dialogVisible.value = false;
    await loadUsers();
  } catch (error) {
    ElMessage.error("保存失败: " + error.message);
  } finally {
    loading.value = false;
  }
};

onMounted(() => {
  loadUsers();
});
</script>

<template>
  <div class="user-management">
    <div class="content-wrapper">
      <el-card shadow="never" class="search-card">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-input
              v-model="searchKeyword"
              placeholder="搜索用户名或邮箱"
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
                新增用户
              </el-button>
            </el-space>
          </el-col>
        </el-row>
      </el-card>

      <el-card shadow="never" class="table-card">
        <el-table :data="tableData" v-loading="loading" stripe>
          <el-table-column prop="id" label="ID" width="280" />
          <el-table-column prop="userName" label="用户名" width="150" />
          <el-table-column prop="role" label="角色" width="120">
            <template #default="{ row }">
              <el-tag
                :type="row.role === 1 ? 'danger' : 'primary'"
                size="small"
              >
                {{ roleMap[row.role] }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="remark" label="备注" min-width="200" />
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
        width="500px"
        :close-on-click-modal="false"
        align-center
      >
        <el-form :model="formData" label-width="auto" class="dialog-form">
          <el-form-item label="用户名" required>
            <el-input v-model="formData.userName" placeholder="请输入用户名" />
          </el-form-item>
          <el-form-item label="密码" :required="dialogTitle === '新增用户'">
            <el-input
              v-model="formData.password"
              type="password"
              :placeholder="
                dialogTitle === '新增用户'
                  ? '请输入密码（至少6位）'
                  : '留空表示不修改密码'
              "
            />
          </el-form-item>
          <el-form-item label="角色">
            <el-select
              v-model="formData.role"
              placeholder="请选择角色"
              style="width: 100%"
            >
              <el-option label="普通用户" :value="0" />
              <el-option label="管理员" :value="1" />
            </el-select>
          </el-form-item>
          <el-form-item label="备注">
            <el-input
              v-model="formData.remark"
              type="textarea"
              :rows="3"
              placeholder="请输入备注信息"
            />
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
.user-management {
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
